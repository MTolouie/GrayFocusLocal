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
import cv2
import numpy as np


# ---------------------------------------------------------------------------
# Modül düzeyinde oturum deposu (Python motoru yaşadığı sürece kalıcıdır)
# ---------------------------------------------------------------------------
_sessions: dict = {}


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
    """

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
            "processed_count": 0,
            "global_total_pixels": 0,
            "selected_previews": [],  # list[str] – önizleme kimlikleri
            "preview_images": {},     # str -> numpy ndarray (8-bit BGR)
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
            "periodic_previews": session["selected_previews"],
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
                progress_callback(payload)

        # --- Koruma: oturum var mı? ---
        session = _sessions.get(session_id)
        if session is None:
            raise KeyError(f"'{session_id}' oturumu başlatılmamış.")

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
        _output({
            "status": "progress",
            "step": 3,
            "total_steps": 4,
            "message": "Belirtilen aralıktaki pikseller hesaplanıyor...",
        })

        mask            = cv2.inRange(img, min_val, max_val)
        pixels_in_range = int(np.count_nonzero(mask))
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

        # ── Seçim algoritması ──────────────────────────────────────────
        # Dinamik periyodik örnekleme: toplu işlemi eşit bölerek preview_count
        # kadar önizleme üretir. total_expected_images bilinmiyorsa (0) her
        # N. görüntü geri düşüş olarak kullanılır.
        total_expected = session["total_expected_images"]
        preview_count  = session["preview_count"]
        step_size = (
            max(1, total_expected // preview_count)
            if total_expected > 0
            else preview_count
        )
        is_periodic = (current_idx % step_size == 0)

        saved_preview_id = None
        if is_periodic:
            # Yalnızca önizleme için 8-bit'e dönüştür; yukarıdaki tüm
            # hesaplamalar orijinal 16-bit img üzerinde yapıldı.
            img_8bit  = (img >> 8).astype(np.uint8)
            img_color = cv2.cvtColor(img_8bit, cv2.COLOR_GRAY2BGR)
            img_color[mask > 0] = [0, 0, 255]  # aralık piksellerini kırmızı vurgula

            preview_id = os.path.basename(image_path)
            # Görüntüyü doğrudan numpy dizisi olarak sakla (byte kodlaması yok)
            session["preview_images"][preview_id] = img_color
            saved_preview_id = preview_id
            session["selected_previews"].append(preview_id)

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
