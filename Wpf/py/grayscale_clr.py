"""
grayscale_clr.py
================
GrayFocus görüntü işleme motorunun Python.NET (pythonnet) sürümü.

FastAPI HTTP katmanının yerini, C# tarafından `Python.Runtime` aracılığıyla
doğrudan çağrılan sade bir Python sınıfı alır.

Tasarım kuralları (Python.NET en iyi uygulamaları):
  1. JSON serializasyonu yok – metodlar yerel Python nesneleri döndürür
     (dict, list, numpy dizisi) böylece C# tarafında ikincil bir ayrıştırma
     adımı gerekmez.
  2. İlerleme bildirimi yerel dict ile yapılır, JSON dizesi değil.
  3. İstisnalar doğal olarak yayılır – anlamlı hatalar C# tarafında
     PythonException olarak yüzeye çıkar; try/catch bloğu UI geri bildirimini
     yönetir.
  4. Görüntüler byte dizisi değil dosya yolu üzerinden alınır.
  5. Önizleme görüntüleri byte olarak değil doğrudan numpy dizisi olarak döndürülür.

── C# kullanım örneği ───────────────────────────────────────────────────────

    using Python.Runtime;

    PythonEngine.Initialize();
    using (Py.GIL())
    {
        dynamic clr  = Py.Import("grayscale_clr");
        dynamic proc = clr.GrayscaleProcessor();

        // Oturum başlat
        proc.start_session("oturum1", min_val: 100, max_val: 200,
                           total_expected_images: 50, preview_count: 5);

        // İlerleme geri çağırımı – Python her adımda yerel dict ile çağırır.
        // Alan erişimi: payload.status, payload.message gibi dynamic üzerinden.
        Action<dynamic> onProgress = payload =>
        {
            string durum   = payload.status;
            string mesaj   = payload.message;
            Dispatcher.Invoke(() => DurumCubugunuGuncelle(durum, mesaj));
        };

        // Bir görüntüyü işle – dosya yolu gönderilir, yerel dict döner
        dynamic sonuc    = proc.process_image("oturum1", @"C:\foto.tif", onProgress);
        int     pikSayı  = (int)sonuc.image_pixels_in_range;

        // Oturum özetini al – yerel dict döner
        dynamic ozet  = proc.get_session_results("oturum1");
        int     toplam = (int)ozet.global_total_pixels;

        // Belirli bir önizleme görüntüsünü numpy dizisi olarak al
        // (EmguCV veya OpenCvSharp ile doğrudan kullanılabilir)
        dynamic onizlemeImg = proc.get_image("oturum1", "onizleme_oturum1_5.png");

        // Temizlik
        proc.cleanup_session("oturum1");
    }

─────────────────────────────────────────────────────────────────────────────
"""

# pyrefly: ignore [missing-import]
import os
# pyrefly: ignore [missing-import]
import cv2
import numpy as np
import threading


# ---------------------------------------------------------------------------
# GPU yardımcısı
# ---------------------------------------------------------------------------

def _is_gpu_available() -> bool:
    """CuPy kuruluysa ve çalışan bir CUDA aygıtı varsa True döndürür."""
    try:
        import cupy as cp  # type: ignore
        cp.cuda.runtime.getDeviceCount()   # en az 1 CUDA aygıtı gerekli
        return cp.cuda.runtime.getDeviceCount() > 0
    except Exception:
        return False


# ---------------------------------------------------------------------------
# Modül düzeyinde oturum deposu (Python motoru yaşadığı sürece kalıcıdır)
# ---------------------------------------------------------------------------
_sessions: dict = {}


# ---------------------------------------------------------------------------
# Küme kova yardımcıları
# ---------------------------------------------------------------------------

def _slot_for(pixels: int, pmin: int, pmax: int, n_slots: int) -> int:
    """Piksel sayısını [0, n_slots) aralığındaki kovaya atar."""
    if pmin == pmax:
        return 0
    return min(n_slots - 1, (pixels - pmin) * n_slots // (pmax - pmin + 1))


def _slot_center(slot_idx: int, pmin: int, pmax: int, n_slots: int) -> float:
    """Bir kovasının piksel-sayısı eksenindeki orta noktası."""
    width = (pmax - pmin + 1) / n_slots
    return pmin + (slot_idx + 0.5) * width


class GrayscaleProcessor:
    """
    Python.NET aracılığıyla doğrudan gömülmek üzere tasarlanmış durum bilgili
    görüntü işleme sınıfı.

    Tüm genel metodlar eş zamanlı ve GIL güvenlidir; C# tarafından
    `Py.GIL()` ile GIL alındıktan sonra çağrılmalıdır.

    İlerleme bildirimi `progress_callback(payload)` çağrısıyla yapılır;
    `payload` en az şu anahtarları içeren yerel bir Python dict'tir:
        status      : "progress" | "completed" | "error"
        step        : int  (1 tabanlı, yalnızca status == "progress" iken)
        total_steps : int
        message     : str

    "completed" payload'u ayrıca şu sonuç anahtarlarını da içerir:
        session_id              : str
        image_pixels_in_range   : int
        global_total_pixels     : int
        saved_preview_id        : str | None

    Parametreler
    ------------
    use_gpu : bool | None
        True   → GPU (CuPy) zorla kullan; CuPy yoksa RuntimeError fırlatır.
        False  → CPU (NumPy/OpenCV) kullan.
        None   → Otomatik algıla: GPU varsa GPU, yoksa CPU kullan.
    """

    def __init__(self, use_gpu=None):
        if use_gpu is None:
            self.use_gpu: bool = _is_gpu_available()
        else:
            self.use_gpu = bool(use_gpu)

        if self.use_gpu:
            try:
                import cupy as cp  # type: ignore  # noqa: F401
            except ImportError as exc:
                raise RuntimeError(
                    "use_gpu=True ancak CuPy kurulu değil. "
                    "Kurulum: pip install cupy-cuda12x  (veya uygun CUDA sürümü)"
                ) from exc

    @property
    def device_label(self) -> str:
        """'GPU' veya 'CPU' döndürür – UI etiketleri için kullanışlı."""
        return "GPU" if self.use_gpu else "CPU"

    # ------------------------------------------------------------------
    # Oturum yaşam döngüsü
    # ------------------------------------------------------------------

    def start_session(
        self,
        session_id: str,
        min_val: int,
        max_val: int,
        total_expected_images: int = 0,
        preview_count: int = 10,
        reporting_level: str = "steps",
        preview_bit_depth: int = 16,
    ) -> None:
        """
        Yeni bir işleme oturumu oluşturur (veya sıfırlar).

        Aynı session_id zaten aktifse KeyError fırlatır;
        yeniden kullanmak için önce cleanup_session() çağırın.
        """
        _sessions[session_id] = {
            "min_val": min_val,
            "max_val": max_val,
            "total_expected_images": total_expected_images,
            "preview_count": max(1, preview_count),
            "reporting_level": str(reporting_level).lower(),
            "preview_bit_depth": preview_bit_depth,
            "processed_count": 0,
            "global_total_pixels": 0,
            # Küme-kova önizleme durumu
            "preview_images": {},   # id → ndarray (yalnızca kova kazananları)
            "preview_slots":  {},   # slot_idx → {"id": str, "pixels": int}
            "pixel_min": None,      # sıfır-dışı piksel sayılarının cçalışan min’i
            "pixel_max": None,      # sıfır-dışı piksel sayılarının cçalışan max’i
            "lock": threading.Lock(),
        }

    def get_session_results(self, session_id: str) -> dict:
        """
        Toplu işlem tamamlandığında oturumu özetleyen yerel bir dict döndürür.

        Anahtarlar:
            session_id              : str
            total_images_processed  : int
            global_total_pixels     : int
            periodic_previews       : list[str]  – get_image() ile kullanılacak kimlikler

        Oturum bulunamazsa KeyError fırlatır.
        """
        session = _sessions.get(session_id)
        if session is None:
            raise KeyError(f"'{session_id}' oturumu bulunamadı.")

        return {
            "session_id": session_id,
            "total_images_processed": session["processed_count"],
            "global_total_pixels": session["global_total_pixels"],
            # Kova kazananlarını slot indeksine göre sırala
            "periodic_previews": [
                v["id"]
                for _, v in sorted(session["preview_slots"].items())
            ],
        }

    def get_image(self, session_id: str, preview_id: str) -> np.ndarray:
        """
        Belirli bir önizleme görüntüsünü 8-bit BGR numpy dizisi olarak döndürür.

        Oturum veya önizleme kimliği bulunamazsa KeyError fırlatır;
        C# tarafında PythonException olarak yakalanır.
        """
        session = _sessions.get(session_id)
        if session is None:
            raise KeyError(f"'{session_id}' oturumu bulunamadı.")
        img = session["preview_images"].get(preview_id)
        if img is None:
            raise KeyError(
                f"'{preview_id}' önizlemesi '{session_id}' oturumunda bulunamadı."
            )
        return img

    def cleanup_session(self, session_id: str) -> None:
        """
        Oturumu siler ve bellekteki tüm önizleme görüntülerini serbest bırakır.

        Oturum bulunamazsa KeyError fırlatır.
        """
        if session_id not in _sessions:
            raise KeyError(f"'{session_id}' oturumu bulunamadı.")
        del _sessions[session_id]

    # ------------------------------------------------------------------
    # Temel işleme
    # ------------------------------------------------------------------

    def process_image(
        self,
        session_id: str,
        image_path: str,
        progress_callback=None,
    ) -> dict:
        """
        Verilen dosya yolundaki görüntüyü belirtilen oturum içinde işler.

        Parametreler
        ------------
        session_id        : start_session() ile oluşturulan aktif oturum
        image_path        : İşlenecek görüntü dosyasının tam yolu (TIFF, PNG, …)
        progress_callback : Her adımda yerel bir dict argümanıyla çağrılan herhangi
                            bir callable. Eş zamanlı çağrılır, iş parçacığı gerekmez.

        Döndürür
        --------
        Başarıda şu anahtarları içeren yerel bir dict:
            status                  : "completed"
            session_id              : str
            image_pixels_in_range   : int
            global_total_pixels     : int
            saved_preview_id        : str | None

        Fırlatır
        --------
        KeyError        oturum bulunamazsa
        FileNotFoundError  dosya yolu geçersizse
        RuntimeError    görüntü çözümlenemezse
        Diğer istisnalar doğrudan yayılır → C# tarafında PythonException
        """

        def _output(payload: dict) -> None:
            if progress_callback is not None:
                level = session.get("reporting_level", "steps")
                if level == "none":
                    return
                status = payload.get("status", "")
                if level == "completed" and status == "progress":
                    return
                progress_callback(payload)

        # --- Koruma: oturum var mı? ---
        session = _sessions.get(session_id)
        if session is None:
            raise KeyError(f"'{session_id}' oturumu başlatılmamış.")

        with session["lock"]:
            session["processed_count"] += 1
            current_idx = session["processed_count"]

        # ── Adım 1: Dosya okuma ────────────────────────────────────────
        _output({
            "status": "progress",
            "step": 1,
            "total_steps": 4,
            "message": f"Oturum [{session_id}]: #{current_idx}. görüntü okunuyor...",
        })

        img = cv2.imread(image_path, cv2.IMREAD_GRAYSCALE | cv2.IMREAD_ANYDEPTH)

        if img is None:
            raise RuntimeError(
                f"Oturum [{session_id}]: #{current_idx} görüntüsü okunamadı → {image_path}"
            )

        min_val = session["min_val"]
        max_val = session["max_val"]

        # ── Adım 2: Parametreler ───────────────────────────────────────
        _output({
            "status": "progress",
            "step": 2,
            "total_steps": 4,
            "message": (
                f"Oturum [{session_id}]: Parametreler yüklendi → "
                f"Min: {min_val}, Max: {max_val}..."
            ),
        })

        # ── Adım 3: Piksel hesaplama (16-bit tam hassasiyetle) ─────────
        device = "GPU" if self.use_gpu else "CPU"
        _output({
            "status": "progress",
            "step": 3,
            "total_steps": 4,
            "message": f"Belirtilen aralıktaki pikseller hesaplanıyor [{device}]...",
        })

        if self.use_gpu:
            import cupy as cp  # type: ignore
            gpu_img         = cp.asarray(img)
            gpu_mask        = (gpu_img >= min_val) & (gpu_img <= max_val)
            pixels_in_range = int(cp.count_nonzero(gpu_mask))
            mask            = cp.asnumpy(gpu_mask.astype(cp.uint8) * 255)
        else:
            mask            = cv2.inRange(img, min_val, max_val)
            pixels_in_range = int(np.count_nonzero(mask))

        with session["lock"]:
            session["global_total_pixels"] += pixels_in_range

            _output({
                "status": "progress",
                "step": 4,
                "total_steps": 4,
                "message": (
                    f"{pixels_in_range} piksel bulundu. "
                    f"Genel toplam: {session['global_total_pixels']}."
                ),
            })

            # ── Seçim: küme kovaları – sıfır piksellik görüntüler dışlanir ────────
            saved_preview_id = None

            if pixels_in_range > 0:
                px            = pixels_in_range
                n             = session["preview_count"]

                # Çalışan piksel-aralığını güncelle
                range_expanded = False
                if session["pixel_min"] is None or px < session["pixel_min"]:
                    session["pixel_min"] = px
                    range_expanded = True
                if session["pixel_max"] is None or px > session["pixel_max"]:
                    session["pixel_max"] = px
                    range_expanded = True

                pmin = session["pixel_min"]
                pmax = session["pixel_max"]

                if range_expanded and session["preview_slots"]:
                    old_slots = dict(session["preview_slots"])
                    session["preview_slots"] = {}
                    
                    for item in old_slots.values():
                        new_slot = _slot_for(item["pixels"], pmin, pmax, n)
                        existing = session["preview_slots"].get(new_slot)
                        
                        if existing is None:
                            session["preview_slots"][new_slot] = item
                        else:
                            center = _slot_center(new_slot, pmin, pmax, n)
                            if abs(item["pixels"] - center) < abs(existing["pixels"] - center):
                                # item is closer to center: delete existing, keep item
                                if existing["id"] in session["preview_images"]:
                                    del session["preview_images"][existing["id"]]
                                session["preview_slots"][new_slot] = item
                            else:
                                # existing is closer: delete item, keep existing
                                if item["id"] in session["preview_images"]:
                                    del session["preview_images"][item["id"]]

                # Geçerli görüntünün hedef kovasını belirle
                target_slot = _slot_for(px, pmin, pmax, n)
                existing    = session["preview_slots"].get(target_slot)
                center      = _slot_center(target_slot, pmin, pmax, n)

                keep = (
                    existing is None
                    or abs(px - center) < abs(existing["pixels"] - center)
                )

                if keep:
                    # Önizleme formatını seç
                    is_8bit = session.get("preview_bit_depth", 16) == 8
                    
                    if is_8bit:
                        # 8-bit'e dönüştür
                        img_preview = (img >> 8).astype(np.uint8) if img.dtype == np.uint16 else img.astype(np.uint8)
                        img_color = cv2.cvtColor(img_preview, cv2.COLOR_GRAY2BGR)
                        img_color[mask > 0] = [0, 0, 255]
                    else:
                        # 16-bit olarak bırak
                        img_preview = img.copy()
                        if img_preview.dtype != np.uint16:
                            img_preview = img_preview.astype(np.uint16)
                        img_color = cv2.cvtColor(img_preview, cv2.COLOR_GRAY2BGR)
                        # 16-bit'te kırmızı kanal maksimum değeri 65535
                        img_color[mask > 0] = [0, 0, 65535]

                    preview_id = os.path.basename(image_path)

                    if existing is not None:
                        # Mevcut kova sahibini bellekten çıkar
                        del session["preview_images"][existing["id"]]

                    session["preview_images"][preview_id] = img_color
                    session["preview_slots"][target_slot]  = {"id": preview_id, "pixels": px}
                    saved_preview_id = preview_id

            # ── Tamamlandı – yerel dict döndür ────────────────────────────
            result = {
                "status": "completed",
                "session_id": session_id,
                "image_pixels_in_range": pixels_in_range,
                "global_total_pixels": session["global_total_pixels"],
                "saved_preview_id": saved_preview_id,  # çoğu görüntü için None
            }
            _output(result)
            return result

    def process_images(
        self,
        session_id: str,
        image_paths,
        progress_callback=None,
        max_workers: int = None,
    ) -> dict:
        """
        Birden çok görüntüyü veya bir klasörü ThreadPoolExecutor kullanarak paralel (eş zamanlı) olarak işler.
        Oturum nesnesine thread-safe olarak erişilir ve I/O/hesaplama adımlarında GIL serbest bırakılır.

        Parametreler
        ------------
        session_id        : start_session() ile oluşturulan aktif oturum
        image_paths       : İşlenecek görüntü dosya yollarının listesi, tek bir klasör yolu (str), veya her ikisinin karışımı.
        progress_callback : Her görüntü tamamlandığında/güncellendiğinde çağrılan callback
        max_workers       : Eşzamanlı çalışacak maksimum thread sayısı (None ise CPU çekirdek sayısına göre)
        """
        session = _sessions.get(session_id)
        if session is None:
            raise KeyError(f"'{session_id}' oturumu başlatılmamış.")

        import glob

        # Dizin altındaki resimleri listeleme yardımcısı
        def _get_images_from_dir(dpath):
            extensions = ['*.tif', '*.tiff', '*.png', '*.jpg', '*.jpeg', '*.bmp']
            found_paths = []
            for ext in extensions:
                found_paths.extend(glob.glob(os.path.join(dpath, ext)))
                found_paths.extend(glob.glob(os.path.join(dpath, ext.upper())))
            return sorted(list(set(found_paths)))

        # Girdi çözümleme
        resolved_paths = []
        if isinstance(image_paths, str):
            if os.path.isdir(image_paths):
                resolved_paths = _get_images_from_dir(image_paths)
            else:
                resolved_paths = [image_paths]
        else:
            for p in image_paths:
                if os.path.isdir(p):
                    resolved_paths.extend(_get_images_from_dir(p))
                else:
                    resolved_paths.append(p)

        image_paths = resolved_paths

        if not image_paths:
            raise FileNotFoundError(f"Oturum [{session_id}]: İşlenecek geçerli görüntü dosyası bulunamadı.")

        # Eğer oturum oluşturulurken 'total_expected_images' 0 verildiyse ve artık toplam sayıyı
        # biliyorsak, oturumu bu gerçek sayı ile güncelle/yeniden başlat ki step_size doğru hesaplansın.
        with session["lock"]:
            if session.get("total_expected_images", 0) == 0:
                session["total_expected_images"] = len(image_paths)

        from concurrent.futures import ThreadPoolExecutor

        def _worker_task(image_path: str):
            try:
                return self.process_image(session_id, image_path, progress_callback)
            except Exception as e:
                err_payload = {
                    "status": "error",
                    "session_id": session_id,
                    "image_path": image_path,
                    "message": str(e)
                }
                if progress_callback is not None:
                    progress_callback(err_payload)
                return err_payload

        # ThreadPoolExecutor kullanarak işleri paralel koştur
        with ThreadPoolExecutor(max_workers=max_workers) as executor:
            list(executor.map(_worker_task, image_paths))

        return self.get_session_results(session_id)
