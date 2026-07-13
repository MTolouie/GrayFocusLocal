using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using Wpf.Entities;

namespace Wpf.Services.IService;

public interface IRoiProcessorService
{
    Task<RoiResult> CreateRectangleAsync(
         string imagePath,
         RectangleRoi rectangle);

    Task<RoiResult> CreatePolygonAsync(
        string imagePath,
        PolygonRoi polygon);
}