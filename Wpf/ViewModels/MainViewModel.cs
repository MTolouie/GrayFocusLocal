using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.DTOs;
using Wpf.Entities;
using Wpf.Services;
using Wpf.Services.IService;
using Wpf.Views;

namespace Wpf.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IImageProcessingService _processingService;
        private readonly IRoiProcessorService _roiProcessorService;
        private readonly IResultWindowFactory _resultWindowFactory;

        // The most recently computed ROI: pixel crop/mask, min/max, and the
        // exact TIFF bytes that will be written to a temp file and handed to
        // process_image() unchanged.
        private RoiResult? _currentRoiResult;

        // --- ABORT / CANCEL SUPPORT ---
        // Cooperative cancellation for the batch loop in SendImageAsync(). The
        // Python call for the image currently in flight cannot be forcibly
        // killed (it's a blocking call under the GIL), so Abort stops the loop
        // from starting the *next* image and then tears down everything that
        // has already accumulated (queue, counters, RAM session state).
        private CancellationTokenSource? _cts;

        // Drives the Abort button's IsEnabled: false at startup, true while a
        // scan is running, false again once the batch completes or is aborted.

        // Cutoff Inputs
        [ObservableProperty] private int _minValue = 10;
        [ObservableProperty] private int _maxValue = 150;
        [ObservableProperty] private string _currentStatusMessage = "Ready";

        // --- MULTI-IMAGE BATCH PROPERTIES ---
        public ObservableCollection<string> SelectedImagesQueue { get; } = new();

        // CHANGED: used to store fully-formed HTTP preview URLs
        // ("http://127.0.0.1:8000/image?session_id=...&preview_id=...").
        // There's no server to build a URL for anymore — this now stores the
        // (sessionId, previewId) pairs ResultViewModel needs to call
        // GetPreviewImageAsync itself.
        private readonly List<(string SessionId, string PreviewId)> _processedResultsPaths = new();

        [ObservableProperty] private string? _imagePath;
        [ObservableProperty] private string _imageDisplayTitle = "Original Photo";

        // --- SELECTION MOUSE STATE PROPERTIES ---
        private Point _startPoint;
        private bool _isDragging;
        private bool _isFreehandDrawing;
        private Point _lastFreehandPoint;

        // CHANGED: Dynamically adjusts coordinate distance threshold based on ZoomScale
        // so drawing curves remains highly responsive and ultra-precise when zoomed way in.
        private double FreehandMinPointDistance => ZoomScale > 0 ? 1.5 / ZoomScale : 1.5;

        // Fill used only once a freehand trace is actually closed (mirrors the
        // Fill="#33F59E0B" used for the polygon tool). Frozen for perf since it
        // never changes and gets assigned repeatedly.
        private static readonly Brush ClosedFreehandFillBrush = CreateClosedFreehandFillBrush();

        private static Brush CreateClosedFreehandFillBrush()
        {
            var brush = new SolidColorBrush(Color.FromArgb(0x33, 0xF5, 0x9E, 0x0B));
            brush.Freeze();
            return brush;
        }

        [ObservableProperty] private double _rectLeft;
        [ObservableProperty] private double _rectTop;
        [ObservableProperty] private double _rectWidth;
        [ObservableProperty] private double _rectHeight;
        [ObservableProperty] private Visibility _rectVisibility = Visibility.Collapsed;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PolygonStrokeThickness))]
        [NotifyPropertyChangedFor(nameof(LineStrokeThickness))]
        private double _zoomScale = 1.0;

        public double PolygonStrokeThickness => ZoomScale > 0 ? 1.5 / ZoomScale : 1.5;
        public double LineStrokeThickness => ZoomScale > 0 ? 1.5 / ZoomScale : 1.5;

        [ObservableProperty] private bool _isRectangleMode = true;
        [ObservableProperty] private bool _isPolygonMode = false;
        [ObservableProperty] private bool _isFreehandMode = false;

        [ObservableProperty] private PointCollection _polygonPoints = new PointCollection();
        [ObservableProperty] private Visibility _polygonVisibility = Visibility.Collapsed;

        [ObservableProperty] private PointCollection _freehandPoints = new PointCollection();
        [ObservableProperty] private Visibility _freehandVisibility = Visibility.Collapsed;
        [ObservableProperty] private Brush _freehandFillBrush = Brushes.Transparent;

        // Track the live drawing line from the last clicked vertex to the active mouse cursor position
        [ObservableProperty] private double _tempLineX1;
        [ObservableProperty] private double _tempLineY1;
        [ObservableProperty] private double _tempLineX2;
        [ObservableProperty] private double _tempLineY2;
        [ObservableProperty] private Visibility _tempLineVisibility = Visibility.Collapsed;

        // --- RENDERED TRACKING BOUNDS FOR MVVM COORDINITE NORMALIZATION ---
        [ObservableProperty] private double _renderedImageWidth;
        [ObservableProperty] private double _renderedImageHeight;

        // --- SELECTION INTENSITY METRICS ---
        [ObservableProperty] private int _selectedMinIntensity;
        [ObservableProperty] private int _selectedMaxIntensity;

        public ObservableCollection<string> ExecutionLog { get; } = new();

        // --- GEOMETRIC CALCULATIONS ---
        [ObservableProperty] private double _fodValue = 181.4; // Default example value
        [ObservableProperty] private double _fddValue = 448.7; // Default example value
        [ObservableProperty] private double _detectorPixelSize = 49.50; // Default 50 microns

        [ObservableProperty] private string _calculatedResolutionMessage = "Real Resolution: N/A";

        // These were being deconstructed into on line ~109 but were never declared,
        // which is what was causing the compile errors.
        [ObservableProperty] private double _objectPixelSizeMicrons;
        [ObservableProperty] private double _magnification;

        // --- VOLUME CALCULATION ---
        // Running total of "in-range" pixels across every processed slice in the batch.
        // Each in-range pixel in a slice is treated as one voxel (assuming isotropic
        // voxels, i.e. the slice spacing equals the in-plane pixel size).
        [ObservableProperty] private long _totalPixelsInRange;
        [ObservableProperty] private string _calculatedVolumeMessage = "Total Volume: N/A";

        // How many preview images the user wants the API to send back
        [ObservableProperty] private int _previewCount = 3;

        // ADD THIS LINE FOR THE PROGRESS BAR
        [ObservableProperty] private double _progressValue;

        [ObservableProperty] private bool _isTerminalVisible = true;
        [ObservableProperty] private bool _isProgressBarVisible = true;

        [ObservableProperty]
        private bool _isGpuSupported = false;

        [ObservableProperty]
        private int _selectedDevice = 0; // Default to CPU (0)

        public bool? SelectedUseGpu => SelectedDevice switch
        {
            0 => false, // CPU
            1 => IsGpuSupported ? true : false, // Fallback to CPU if GPU unsupported
            _ => null   // Auto
        };

        private bool _isProcessing;

        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetProperty(ref _isProcessing, value))
                {
                    // Marshal the state change update back to the UI Thread instantly!
                    System.Windows.Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                    {
                        AbortProcessingCommand.NotifyCanExecuteChanged();
                        SendImageCommand.NotifyCanExecuteChanged(); // Disables the Scan button during processing too!
                    }));
                }
            }
        }

        private int MetaDataMinValue { get; set; } = 0;
        private int MetaDataMaxValue { get; set; } = 0;

        public void SetGpuSupport(bool gpuSupported)
        {
            IsGpuSupported = gpuSupported;

            if (IsGpuSupported)
            {
                SelectedDevice = 1; // Auto-select GPU
            }
            else
            {
                SelectedDevice = 0; // Force CPU mode if unsupported (e.g. CUDA < 11)
            }
        }

        public void InitializeHardwareSupport(bool gpuSupported)
        {
            IsGpuSupported = gpuSupported;
            if (!IsGpuSupported)
            {
                SelectedDevice = 0; // Fallback to CPU
            }
        }
        partial void OnIsTerminalVisibleChanged(bool value)
        {
            if (!value)
            {
                IsProgressBarVisible = false;
            }
        }

        // Automatically recalculates whenever any geometric field changes
        partial void OnFodValueChanged(double value) => RecalculateObjectResolution();
        partial void OnFddValueChanged(double value) => RecalculateObjectResolution();
        partial void OnDetectorPixelSizeChanged(double value) => RecalculateObjectResolution();

        public MainViewModel(
            IImageProcessingService processingService,
            IRoiProcessorService roiProcessorService,
            IResultWindowFactory resultWindowFactory)
        {
            _processingService = processingService;
            _roiProcessorService = roiProcessorService;
            _resultWindowFactory = resultWindowFactory;

            // Field initializers (FodValue = 100.0, etc.) don't go through the
            // property setters, so the OnXChanged hooks never fire at startup.
            // Run it once manually so ObjectPixelSizeMicrons isn't left at 0.
            RecalculateObjectResolution();
        }

        private void RecalculateObjectResolution()
        {
            if (FodValue <= 0 || FddValue <= 0)
            {
                CalculatedResolutionMessage = "Real Resolution: Invalid Dimensions";
                return;
            }

            // 1. M = FDD / FOD
            // 2. Object Pixel Size = Detector Pixel Size / M

            (ObjectPixelSizeMicrons, Magnification) = GetObjectPixelSizeMicrons();
            var pixelSizeMicorons = ObjectPixelSizeMicrons.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            var magnification = Magnification.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            CalculatedResolutionMessage = $"Effective Res: {pixelSizeMicorons} µm/px (M: {magnification:F2}x)";
        }

        public (double ObjectPixelSizeMicrons, double Magnification) GetObjectPixelSizeMicrons()
        {
            if (FodValue <= 0 || FddValue <= 0)
                return (0, 0);

            double magnification = FddValue / FodValue;
            double objectPixelSizeMicrons = DetectorPixelSize / magnification;

            return (objectPixelSizeMicrons, magnification);
        }

        // Treats every "in-range" pixel across every processed slice as one voxel.
        // Voxel volume = ObjectPixelSizeMicrons^3 (assumes isotropic voxels, i.e.
        // slice spacing == in-plane pixel size). Converts µm^3 -> m^3
        // (1 µm = 1e-6 m, so 1 µm^3 = 1e-18 m^3).
        private void RecalculateTotalVolume()
        {
            if (ObjectPixelSizeMicrons <= 0 || TotalPixelsInRange <= 0)
            {
                CalculatedVolumeMessage = "Total Volume: N/A";
                return;
            }

            double voxelVolumeCubicMicrons = Math.Pow(ObjectPixelSizeMicrons, 3);
            double totalVolumeCubicMicrons = TotalPixelsInRange * voxelVolumeCubicMicrons;
            double totalVolumeCubicMeters = totalVolumeCubicMicrons / 1000000000;

            CalculatedVolumeMessage = $"Total Volume: {totalVolumeCubicMeters.ToString("F12", System.Globalization.CultureInfo.InvariantCulture)} mm³ ({TotalPixelsInRange} voxels)";
        }

        // --- file SELECTION COMMAND ---
        [RelayCommand]
        private void SelectImage()
        {
            var fileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select a Reference TIFF Photo (its folder becomes the batch)",
                Filter = "TIFF Images (*.tif;*.tiff)|*.tif;*.tiff",
                Multiselect = false
            };

            if (fileDialog.ShowDialog() == true)
            {
                string selectedFile = fileDialog.FileName;
                string? folderPath = Path.GetDirectoryName(selectedFile);

                if (string.IsNullOrEmpty(folderPath))
                {
                    MessageBox.Show("Could not resolve the folder for the selected file.",
                                     "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var tiffFiles = Directory
                    .EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f =>
                        f.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f)
                    .ToList();

                if (tiffFiles.Count == 0)
                {
                    MessageBox.Show(
                        "No TIFF files (.tif / .tiff) were found in the selected photo's folder.",
                        "Empty Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                SelectedImagesQueue.Clear();
                _processedResultsPaths.Clear();
                ExecutionLog.Clear();
                TotalPixelsInRange = 0;
                CalculatedVolumeMessage = "Total Volume: N/A";

                foreach (string file in tiffFiles)
                    SelectedImagesQueue.Add(file);

                // Move the reference photo to the front so LoadNextImageFromQueue()
                // (which always shows SelectedImagesQueue[0]) displays THIS image
                // for ROI drawing, instead of whichever file sorts first alphabetically.
                if (SelectedImagesQueue.Remove(selectedFile))
                    SelectedImagesQueue.Insert(0, selectedFile);

                ExecutionLog.Add($"[Local] Folder: {folderPath}");
                ExecutionLog.Add($"[Local] Reference photo: {Path.GetFileName(selectedFile)}");
                ExecutionLog.Add($"[Local] Loaded {SelectedImagesQueue.Count} TIFF file(s) into processing batch.");
                LoadNextImageFromQueue();
            }
        }

        private void LoadNextImageFromQueue()
        {
            SelectedMinIntensity = 0;
            SelectedMaxIntensity = 0;
            _currentRoiResult = null;

            if (SelectedImagesQueue.Count > 0)
            {
                ImagePath = SelectedImagesQueue[0];
                RectVisibility = Visibility.Collapsed;
                PolygonPoints = new PointCollection();
                PolygonVisibility = Visibility.Collapsed;
                TempLineVisibility = Visibility.Collapsed;
                FreehandPoints = new PointCollection();
                FreehandVisibility = Visibility.Collapsed;
                _isFreehandDrawing = false;
                FreehandFillBrush = Brushes.Transparent;
                ZoomScale = 1.0;
                ImageDisplayTitle = $"Original Photo ({Path.GetFileName(ImagePath)}) - {SelectedImagesQueue.Count} remaining";
                CurrentStatusMessage = $"Ready for selection on: {Path.GetFileName(ImagePath)}";
            }
            else
            {
                ImagePath = null;
                ImageDisplayTitle = "Original Photo (No images left in queue)";
                CurrentStatusMessage = "All images processed!";
            }
        }

        [RelayCommand]
        private void CanvasMouseWheel(MouseWheelEventArgs e)
        {
            if (e == null) return;

            // 1. Find the ScrollViewer
            ScrollViewer? scrollViewer = null;
            DependencyObject current = e.Source as DependencyObject;
            while (current != null)
            {
                if (current is ScrollViewer sv) { scrollViewer = sv; break; }
                current = VisualTreeHelper.GetParent(current);
            }

            if (scrollViewer == null) return;

            // 2. Get mouse position relative to the ScrollViewer's viewable screen space
            Point mousePosView = e.GetPosition(scrollViewer);

            // 3. Calculate exactly where the mouse is pointing on the actual unscaled image coordinates
            double mouseInContentX = scrollViewer.HorizontalOffset + mousePosView.X;
            double mouseInContentY = scrollViewer.VerticalOffset + mousePosView.Y;

            double oldScale = ZoomScale;

            // 4. Update the zoom scale by your 0.50 steps
            if (e.Delta > 0)
            {
                if (ZoomScale < 500.0) ZoomScale += 2.50;
                if (ZoomScale > 100.0) ZoomScale += 10.00;
            }
            else
            {
                if (ZoomScale > 1.50) ZoomScale -= 2.50;
                if (ZoomScale > 100.0) ZoomScale -= 10.00;
            }
            // 5. Calculate the ratio change
            double scaleRatio = ZoomScale / oldScale;

            // 6. Predict where that exact coordinate point will land in the new scaled world
            double newScrollX = (mouseInContentX * scaleRatio) - mousePosView.X;
            double newScrollY = (mouseInContentY * scaleRatio) - mousePosView.Y;

            // 7. Instantly move the scroll bars to match
            scrollViewer.ScrollToHorizontalOffset(newScrollX);
            scrollViewer.ScrollToVerticalOffset(newScrollY);

            e.Handled = true;
        }
        [RelayCommand]
        private void RemoveShape()
        {
            if (string.IsNullOrEmpty(ImagePath)) return;

            _isDragging = false;
            RectWidth = 0;
            RectHeight = 0;
            RectVisibility = Visibility.Collapsed;

            PolygonPoints = new PointCollection(); // Reassigned (Correct)
            PolygonVisibility = Visibility.Collapsed;

            TempLineVisibility = Visibility.Collapsed;
            TempLineX1 = 0; TempLineY1 = 0;
            TempLineX2 = 0; TempLineY2 = 0;

            FreehandPoints = new PointCollection();
            FreehandVisibility = Visibility.Collapsed;
            _isFreehandDrawing = false;
            FreehandFillBrush = Brushes.Transparent;

            SelectedMinIntensity = 0;
            SelectedMaxIntensity = 0;
            _currentRoiResult = null;

            CurrentStatusMessage = $"Selection cleared for {Path.GetFileName(ImagePath)}. Draw a new shape.";
        }

        // --- MOUSE BEHAVIOR COMMANDS ---
        [RelayCommand]
        private void CanvasMouseDown(MouseEventArgs e)
        {
            if (string.IsNullOrEmpty(ImagePath)) return;

            var canvas = e.Source as IInputElement;
            if (canvas == null) return;

            Point clickPoint = e.GetPosition(canvas);

            if (IsRectangleMode)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    _isDragging = true;
                    _startPoint = clickPoint;

                    RectLeft = _startPoint.X;
                    RectTop = _startPoint.Y;
                    RectWidth = 0;
                    RectHeight = 0;
                    RectVisibility = Visibility.Visible;
                    PolygonVisibility = Visibility.Collapsed;

                    SelectedMinIntensity = 0;
                    SelectedMaxIntensity = 0;
                    _currentRoiResult = null;
                }
            }
            else if (IsPolygonMode)
            {
                if (e.RightButton == MouseButtonState.Pressed || (e as MouseButtonEventArgs)?.ChangedButton == MouseButton.Right)
                {
                    if (PolygonPoints != null && PolygonPoints.Count > 2)
                    {
                        if (PolygonPoints[PolygonPoints.Count - 1] != PolygonPoints[0])
                        {
                            var closedPoints = new PointCollection(PolygonPoints)
                            {
                                PolygonPoints[0]
                            };

                            PolygonPoints = closedPoints;
                            PolygonVisibility = Visibility.Visible;

                            TempLineVisibility = Visibility.Collapsed;
                            TempLineX1 = 0; TempLineY1 = 0;
                            TempLineX2 = 0; TempLineY2 = 0;

                            var pointsSummary = string.Join(" -> ", System.Linq.Enumerable.Select(PolygonPoints, p => $"({Math.Round(p.X)},{Math.Round(p.Y)})"));
                            CurrentStatusMessage = "Polygon closed perfectly. Calculating pixel metrics...";
                            ExecutionLog.Add($"[Selection] Polygon marked: {pointsSummary}");

                            _ = CalculateRegionIntensitiesAsync();
                        }
                    }
                    return;
                }

                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    if (PolygonPoints != null && PolygonPoints.Count > 1 && PolygonPoints[PolygonPoints.Count - 1] == PolygonPoints[0])
                    {
                        PolygonPoints = new PointCollection();
                        RectVisibility = Visibility.Collapsed;
                        PolygonVisibility = Visibility.Visible;

                        SelectedMinIntensity = 0;
                        SelectedMaxIntensity = 0;
                        _currentRoiResult = null;
                    }
                    else if (PolygonPoints == null || PolygonPoints.Count == 0)
                    {
                        RectVisibility = Visibility.Collapsed;
                        PolygonVisibility = Visibility.Visible;
                        PolygonPoints = new PointCollection();
                    }

                    var technicalUpdatedPoints = new PointCollection(PolygonPoints)
                    {
                        clickPoint
                    };

                    PolygonPoints = technicalUpdatedPoints;

                    TempLineX1 = clickPoint.X;
                    TempLineY1 = clickPoint.Y;
                    TempLineX2 = clickPoint.X;
                    TempLineY2 = clickPoint.Y;
                    TempLineVisibility = Visibility.Visible;

                    CurrentStatusMessage = $"Added vertex point {PolygonPoints.Count}. Right-click anywhere to complete the shape.";
                }
            }
            else if (IsFreehandMode)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    _isFreehandDrawing = true;
                    _lastFreehandPoint = clickPoint;

                    RectVisibility = Visibility.Collapsed;
                    PolygonVisibility = Visibility.Collapsed;
                    TempLineVisibility = Visibility.Collapsed;

                    FreehandPoints = new PointCollection { clickPoint };
                    FreehandVisibility = Visibility.Visible;
                    FreehandFillBrush = Brushes.Transparent;

                    SelectedMinIntensity = 0;
                    SelectedMaxIntensity = 0;
                    _currentRoiResult = null;

                    CurrentStatusMessage = "Drawing freehand shape... release the mouse to close it.";
                }
            }
        }

        partial void OnIsRectangleModeChanged(bool value)
        {
            if (value)
            {
                PolygonPoints = new PointCollection();
                PolygonVisibility = Visibility.Collapsed;
                TempLineVisibility = Visibility.Collapsed;

                FreehandPoints = new PointCollection();
                FreehandVisibility = Visibility.Collapsed;
                _isFreehandDrawing = false;
                FreehandFillBrush = Brushes.Transparent;

                SelectedMinIntensity = 0;
                SelectedMaxIntensity = 0;
                _currentRoiResult = null;
            }
        }

        partial void OnIsPolygonModeChanged(bool value)
        {
            if (value)
            {
                RectVisibility = Visibility.Collapsed;
                RectWidth = 0;
                RectHeight = 0;

                FreehandPoints = new PointCollection();
                FreehandVisibility = Visibility.Collapsed;
                _isFreehandDrawing = false;
                FreehandFillBrush = Brushes.Transparent;

                SelectedMinIntensity = 0;
                SelectedMaxIntensity = 0;
                _currentRoiResult = null;
            }
        }

        partial void OnIsFreehandModeChanged(bool value)
        {
            if (value)
            {
                RectVisibility = Visibility.Collapsed;
                RectWidth = 0;
                RectHeight = 0;

                PolygonPoints = new PointCollection();
                PolygonVisibility = Visibility.Collapsed;
                TempLineVisibility = Visibility.Collapsed;

                SelectedMinIntensity = 0;
                SelectedMaxIntensity = 0;
                _currentRoiResult = null;
            }
        }

        [RelayCommand]
        private void CanvasMouseMove(MouseEventArgs e)
        {
            var canvas = e.Source as IInputElement;
            if (canvas == null) return;

            Point currentPoint = e.GetPosition(canvas);

            if (IsRectangleMode && _isDragging)
            {
                RectLeft = Math.Min(_startPoint.X, currentPoint.X);
                RectTop = Math.Min(_startPoint.Y, currentPoint.Y);
                RectWidth = Math.Abs(currentPoint.X - _startPoint.X);
                RectHeight = Math.Abs(currentPoint.Y - _startPoint.Y);
            }
            else if (IsPolygonMode && TempLineVisibility == Visibility.Visible)
            {
                TempLineX2 = currentPoint.X;
                TempLineY2 = currentPoint.Y;
            }
            else if (IsFreehandMode && _isFreehandDrawing)
            {
                double dx = currentPoint.X - _lastFreehandPoint.X;
                double dy = currentPoint.Y - _lastFreehandPoint.Y;

                // CHANGED: Capturing current calculated threshold dynamically instead of constant
                double minDistance = FreehandMinPointDistance;

                if ((dx * dx) + (dy * dy) >= minDistance * minDistance)
                {
                    var updatedPoints = new PointCollection(FreehandPoints)
                    {
                        currentPoint
                    };

                    FreehandPoints = updatedPoints;
                    _lastFreehandPoint = currentPoint;
                }
            }
        }

        [RelayCommand]
        private void CanvasMouseUp()
        {
            if (IsFreehandMode && _isFreehandDrawing)
            {
                _isFreehandDrawing = false;

                if (FreehandPoints != null && FreehandPoints.Count > 2)
                {
                    // Close the traced shape back to its starting point
                    var closedPoints = new PointCollection(FreehandPoints)
                    {
                        FreehandPoints[0]
                    };
                    FreehandPoints = closedPoints;
                    FreehandFillBrush = ClosedFreehandFillBrush;

                    CurrentStatusMessage = $"Freehand region marked for {Path.GetFileName(ImagePath)}. Calculating pixel metrics...";
                    _ = CalculateRegionIntensitiesAsync();
                }
                else
                {
                    FreehandVisibility = Visibility.Collapsed;
                    FreehandFillBrush = Brushes.Transparent;
                    CurrentStatusMessage = "Freehand shape was too small — try drawing a larger area.";
                }

                return;
            }

            if (!_isDragging) return;
            _isDragging = false;

            CurrentStatusMessage = $"Region marked for {Path.GetFileName(ImagePath)}. Calculating pixel metrics...";

            _ = CalculateRegionIntensitiesAsync();
        }

        private async Task CalculateRegionIntensitiesAsync()
        {
            if (string.IsNullOrEmpty(ImagePath)) return;

            int nativeWidth = 0;
            int nativeHeight = 0;

            try
            {
                using (var tiff = BitMiracle.LibTiff.Classic.Tiff.Open(ImagePath, "r"))
                {
                    if (tiff != null)
                    {
                        nativeWidth = tiff.GetField(BitMiracle.LibTiff.Classic.TiffTag.IMAGEWIDTH)[0].ToInt();
                        nativeHeight = tiff.GetField(BitMiracle.LibTiff.Classic.TiffTag.IMAGELENGTH)[0].ToInt();
                    }
                }
            }
            catch
            {
                return;
            }

            if (nativeWidth == 0 || nativeHeight == 0) return;

            double displayWidth = RenderedImageWidth > 0 ? RenderedImageWidth : nativeWidth;
            double displayHeight = RenderedImageHeight > 0 ? RenderedImageHeight : nativeHeight;

            double scaleX = (double)nativeWidth / displayWidth;
            double scaleY = (double)nativeHeight / displayHeight;

            // --- CASE 1: RECTANGLE SELECTION ---
            if (IsRectangleMode && RectWidth > 0 && RectHeight > 0)
            {
                var scaledRect = new RectangleRoi(
                    RectLeft * scaleX,
                    RectTop * scaleY,
                    RectWidth * scaleX,
                    RectHeight * scaleY
                );

                CurrentStatusMessage = "Computing rectangle intensities...";
                _currentRoiResult = await _roiProcessorService.CreateRectangleAsync(ImagePath, scaledRect);

                SelectedMinIntensity = _currentRoiResult.MinValue;
                SelectedMaxIntensity = _currentRoiResult.MaxValue;
                MetaDataMaxValue = _currentRoiResult.MaxValue;
                MetaDataMinValue = _currentRoiResult.MinValue;
                CurrentStatusMessage = "Analysis complete.";
            }
            else if (IsPolygonMode && PolygonPoints != null && PolygonPoints.Count > 2)
            {
                // Convert screen space Points array into raw pixel space RoiPoint array
                var scaledPoints = new List<Wpf.Entities.RoiPoint>();
                foreach (var pt in PolygonPoints)
                {
                    // Explicitly map coordinates into your custom domain entity object type
                    scaledPoints.Add(new Wpf.Entities.RoiPoint(pt.X * scaleX, pt.Y * scaleY));
                }

                var polygonRoi = new PolygonRoi(scaledPoints);

                CurrentStatusMessage = "Computing polygon intensities...";
                _currentRoiResult = await _roiProcessorService.CreatePolygonAsync(ImagePath, polygonRoi);

                SelectedMinIntensity = _currentRoiResult.MinValue;
                SelectedMaxIntensity = _currentRoiResult.MaxValue;
                MetaDataMaxValue = _currentRoiResult.MaxValue;
                MetaDataMinValue = _currentRoiResult.MinValue;
                CurrentStatusMessage = "Analysis complete.";
            }
            else if (IsFreehandMode && FreehandPoints != null && FreehandPoints.Count > 2)
            {
                // A closed freehand trace is just a many-vertex polygon, so it
                // reuses the exact same ROI pipeline as the polygon tool.
                var scaledPoints = new List<Wpf.Entities.RoiPoint>();
                foreach (var pt in FreehandPoints)
                {
                    scaledPoints.Add(new Wpf.Entities.RoiPoint(pt.X * scaleX, pt.Y * scaleY));
                }

                var freehandRoi = new PolygonRoi(scaledPoints);

                CurrentStatusMessage = "Computing freehand region intensities...";
                _currentRoiResult = await _roiProcessorService.CreatePolygonAsync(ImagePath, freehandRoi);

                SelectedMinIntensity = _currentRoiResult.MinValue;
                SelectedMaxIntensity = _currentRoiResult.MaxValue;
                MetaDataMaxValue = _currentRoiResult.MaxValue;
                MetaDataMinValue = _currentRoiResult.MinValue;
                CurrentStatusMessage = "Analysis complete.";
            }
        }

        // --- PIPELINE BATCH EXECUTION ---
        [RelayCommand]
        private async Task SendImageAsync()
        {
            if (string.IsNullOrEmpty(ImagePath))
            {
                MessageBox.Show("No active image to process.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_currentRoiResult == null || !_currentRoiResult.HasPixels)
            {
                MessageBox.Show("Please draw a valid selection area first.", "Invalid Selection Bounds", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Rely on the ViewModel's current Min/Max properties (whether calculated or user-edited)
            int minVal = SelectedMinIntensity;
            int maxVal = SelectedMaxIntensity;

            string? folderPath = Path.GetDirectoryName(ImagePath);
            if (string.IsNullOrEmpty(folderPath)) return;

            ExecutionLog.Clear();
            _processedResultsPaths.Clear();
            TotalPixelsInRange = 0;
            CalculatedVolumeMessage = "Total Volume: N/A";

            // 1. INITIALIZE PROGRESS BAR
            ProgressValue = 0;

            _cts = new CancellationTokenSource();
            var cts = _cts; // Local copy for safety inside callbacks
            IsProcessing = true;

            var sessionId = Guid.NewGuid().ToString();
            var scanRequest = new ScanRequestDTO
            {
                SessionId = sessionId,
                MinValue = minVal,
                MaxValue = maxVal,
                total_expected_images = SelectedImagesQueue.Count,
                preview_count = PreviewCount,
                UseGpu = SelectedUseGpu
            };

            var result = await _processingService.SendDataAsync(scanRequest);
            if (result.status != "started")
            {
                CurrentStatusMessage = "Something Went Wrong.";
                IsProcessing = false;
                return;
            }

            var uiScheduler = TaskScheduler.FromCurrentSynchronizationContext();

            // Keep track of how many items have completed processing
            int processedItemsCount = 0;
            int totalImages = SelectedImagesQueue.Count;

            try
            {
                Action<ProcessingProgress> progressReporter = (progress) =>
                {
                    // --- CRITICAL CANCELLATION CHECK ---
                    // If user pressed "Abort", this callback intercepts the next reporting thread
                    // and halts the entire operation instantly.
                    if (cts.Token.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cts.Token);
                    }

                    // Skip all UI thread marshaling completely if both features are turned off!
                    if ((progress.Status == "progress" || progress.Status == "error") && !IsTerminalVisible)
                    {
                        return;
                    }

                    Task.Factory.StartNew(() =>
                    {
                        if (progress.Status == "progress")
                        {
                            // Only touch the status text / log when the terminal is visible —
                            // avoids a PropertyChanged + binding update on every progress tick.
                            if (IsTerminalVisible)
                            {
                                CurrentStatusMessage = progress.Message;
                                ExecutionLog.Add($"[Step {progress.Step}/{progress.TotalSteps}] {progress.Message}");
                            }
                        }
                        else if (progress.Status == "completed")
                        {
                            processedItemsCount++;
                            int remaining = totalImages - processedItemsCount;
                            // PERFORMANCE CAP: Only calculate progress percentage if the progress bar is active
                            if (IsProgressBarVisible && totalImages > 0)
                            {
                                ProgressValue = ((double)processedItemsCount / totalImages) * 100;
                            }

                            // Update metrics
                            TotalPixelsInRange += progress.ImagePixelsInRange;
                            RecalculateTotalVolume();

                            if (IsTerminalVisible)
                            {
                                ExecutionLog.Add($"✨ [Processed Slice] Target Found: {progress.ImagePixelsInRange} px");
                            }

                            // Try to advance the UI view to display the image that just finished
                            // or remove it gracefully from the visual collection queue.
                            //if (SelectedImagesQueue.Count > 0)
                            //{
                            //    // Match by filename logic to safely remove the file that just finished
                            //    string completedFile = SelectedImagesQueue.FirstOrDefault(f =>
                            //        Path.GetFileName(f).Equals(progress.SavedPreviewId, StringComparison.OrdinalIgnoreCase) ||
                            //        f.EndsWith(progress.SavedPreviewId ?? "", StringComparison.OrdinalIgnoreCase));

                            //    if (!string.IsNullOrEmpty(completedFile))
                            //    {
                            //        SelectedImagesQueue.Remove(completedFile);
                            //    }
                            //}

                            // Update titles dynamically to reflect true batch progression status
                            if (SelectedImagesQueue.Count > 0)
                            {
                                ImagePath = SelectedImagesQueue[0]; // Still advance the preview image

                                // Only rebuild the title string when the terminal is visible — skips
                                // a string interpolation + PropertyChanged on every processed slice.

                                ImageDisplayTitle = $"Original Photo ({Path.GetFileName(ImagePath)}) - {remaining} remaining";

                            }
                        }
                        else if (progress.Status == "error")
                        {
                            if (IsTerminalVisible)
                            {
                                ExecutionLog.Add($"❌ Error processing slice: {progress.Message}");
                            }
                        }
                    }, CancellationToken.None, TaskCreationOptions.None, uiScheduler);
                };

                CurrentStatusMessage = "Bulk processing entire directory in parallel...";

                // Execute the bulk folder background task
                SessionResultsDTO sessionSummary = await _processingService.ProcessFolderAsync(sessionId, folderPath, progressReporter);

                // --- BATCH SUCCESS ROUTINE ---
                IsProcessing = false;
                SelectedImagesQueue.Clear();
                ImagePath = string.Empty;
                ImageDisplayTitle = "Original Photo (All images processed)";
                CurrentStatusMessage = "All images processed successfully!";
                if (IsTerminalVisible)
                {
                    ExecutionLog.Add("--- PARALLEL BATCH RUN COMPLETE ---");
                }

                // Ensure progress finishes completely filled out
                ProgressValue = 100;

                TotalPixelsInRange = sessionSummary.GlobalTotalPixels;
                RecalculateTotalVolume();
                ExecutionLog.Add($"📦 {CalculatedVolumeMessage}");

                ClearSelectionAndShapes();

                _processedResultsPaths.Clear();
                for (int i = 0; i < sessionSummary.PeriodicPreviews.Count; i++)
                {
                    string previewId = sessionSummary.PeriodicPreviews[i];
                    _processedResultsPaths.Add((sessionId, previewId));
                }



                // Prepare the Metadata DTO to pass over to ResultViewModel

                var batchMetadata = new BatchMetadataDTO
                {
                    SessionId = sessionId,
                    FodValue = FodValue,
                    FddValue = FddValue,
                    ObjectPixelSizeMicrons = ObjectPixelSizeMicrons,
                    Magnification = Magnification,
                    // Explicitly grab the active cutoff values
                    MinValue = MetaDataMinValue,
                    MaxValue = MetaDataMaxValue,
                    PreviewRefs = _processedResultsPaths,
                    TotalPixelInRange = TotalPixelsInRange
                };



                // Create the window with metadata (Update your Factory interface/implementation to accept BatchMetadataDTO)
                var resultWin = _resultWindowFactory.Create(batchMetadata);
                resultWin.Owner = System.Windows.Application.Current.MainWindow;
                resultWin.Closed += async (s, e) =>
                {
                    // Re-activate MainWindow on the UI thread to keep it in the foreground
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        System.Windows.Application.Current.MainWindow?.Activate();
                    });

                    try { await _processingService.CleanUpDataAsync(sessionId); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"CleanUp Error: {ex.Message}"); }
                };

                resultWin.Show();
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex.InnerException is OperationCanceledException)
            {
                // Intercepts the custom cancel throw and safely handles the reset state
                await HandleAbortCleanupAsync(sessionId);
            }
            catch (Exception ex)
            {
                if (IsTerminalVisible)
                {
                    ExecutionLog.Add($"❌ Bulk Pipeline Fault: {ex.Message}");
                }
                CurrentStatusMessage = "Processing error occurred.";
                IsProcessing = false;
                ProgressValue = 0; // Reset bar on crash
            }
        }

        // --- ABORT / CANCEL COMMAND ---
        // Bound to the Abort button, which is only enabled while IsProcessing
        // is true (i.e. between "Scan Target Region" being clicked and the
        // batch finishing). Requests cancellation; the currently in-flight
        // image (if any) is allowed to finish since the Python call is
        // blocking, but no further image is started, and everything gathered
        // so far — queue, running pixel/volume totals, and the Python-side
        // in-memory session (including any analyzed preview sitting there
        // waiting to be pulled) — gets torn down in HandleAbortCleanupAsync.
        [RelayCommand(CanExecute = nameof(CanAbortProcessing))]
        private void AbortProcessing()
        {
            if (!IsProcessing || _cts == null) return;

            CurrentStatusMessage = "Cancelling — finishing current image...";
            ExecutionLog.Add("🛑 Abort requested by user.");
            _cts.Cancel();
        }

        private bool CanAbortProcessing() => IsProcessing;

        // The [ObservableProperty]-generated IsProcessing setter calls this
        // automatically whenever IsProcessing changes, so the Abort button's
        // enabled state stays in sync with the command's own CanExecute
        // instead of fighting a separate IsEnabled binding.

        private async Task HandleAbortCleanupAsync(string sessionId)
        {
            // Erase the analyzed/pending data sitting in the Python engine's
            // RAM for this session (in-progress numpy arrays, previews, etc.)
            try
            {
                await _processingService.CleanUpDataAsync(sessionId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clean up aborted session: {ex.Message}");
            }

            // Reset the batch queue and every accumulated result
            SelectedImagesQueue.Clear();
            _processedResultsPaths.Clear();
            TotalPixelsInRange = 0;
            CalculatedVolumeMessage = "Total Volume: N/A";

            ProgressValue = 0;

            ImagePath = null;
            ImageDisplayTitle = "Original Photo (No images left in queue)";
            ClearSelectionAndShapes();

            ExecutionLog.Add("--- OPERATION ABORTED ---");
            CurrentStatusMessage = "Operation aborted.";

            IsProcessing = false;
            _cts?.Dispose();
            _cts = null;
        }

        // Add this command inside your MainViewModel class

        [RelayCommand]
        private void UpdateRenderedBounds(SizeChangedEventArgs e)
        {
            RenderedImageWidth = e.NewSize.Width;
            RenderedImageHeight = e.NewSize.Height;
        }
        private void ClearSelectionAndShapes()
        {
            RectLeft = 0; RectTop = 0; RectWidth = 0; RectHeight = 0;
            RectVisibility = Visibility.Collapsed;

            PolygonPoints = new PointCollection();
            PolygonVisibility = Visibility.Collapsed;

            TempLineVisibility = Visibility.Collapsed;
            TempLineX1 = 0; TempLineY1 = 0;
            TempLineX2 = 0; TempLineY2 = 0;

            FreehandPoints = new PointCollection();
            FreehandVisibility = Visibility.Collapsed;
            _isFreehandDrawing = false;
            FreehandFillBrush = Brushes.Transparent;

            SelectedMinIntensity = 0;
            SelectedMaxIntensity = 0;
            _currentRoiResult = null;
        }
    }
}