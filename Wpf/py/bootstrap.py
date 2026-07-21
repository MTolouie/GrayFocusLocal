"""
bootstrap.py
============
Bağımlılıkları, kullanıcı kurulum yapmadan, yerel '_deps/' klasörüne
otomatik olarak yükler.

Kullanım — diğer tüm import'lardan ÖNCE her giriş noktasında:

    import bootstrap   # noqa: F401

Neler yapar:
  1. '_deps/' klasörünü sys.path'e ekler (oluşturulmuşsa).
  2. Çekirdek paketleri (numpy, opencv-python) kontrol eder;
     eksikse '_deps/'a yükler.
  3. nvidia-smi çıktısını okuyarak CUDA major sürümünü tespit eder.
     GPU bulunursa sürüme uygun cupy paketini yükler.
  4. Sonraki çalışmalarda hiçbir şey eksik değilse pip HİÇ çalışmaz.

Katman notu:
  Bu betik yalnızca Python yorumlayıcı zaten başlatıldıktan SONRA
  çalışır (PythonEngine.Initialize() arkasından). Python'un kendisini
  bulmak ve python3XX.dll'i yüklemek C# tarafı (PythonEngineService)
  sorumluluğundadır.
"""

from __future__ import annotations

import importlib
import os
import subprocess
import sys

# ---------------------------------------------------------------------------
# Sabitler
# ---------------------------------------------------------------------------

_HERE = os.path.dirname(os.path.abspath(__file__))
_DEPS = os.path.join(_HERE, "_deps")

# Çekirdek paketler — her kullanıcıya yüklenir.
# (Pillow burada YOK — yalnızca geliştirici aracı test_gui.py için gerekli)
_CORE_PACKAGES: list[tuple[str, str]] = [
    # (pip_adı,        import_adı)
    ("numpy==2.5.1",                 "numpy"),
    ("opencv-python==5.0.0.93",      "cv2"),
]

# CUDA major sürümü → cupy pip paketi
# [ctk] eki: CUDA Toolkit kurulu olmadan yalnızca sürücü ile çalışır.
# Güncel liste: https://docs.cupy.dev/en/stable/install.html
_CUPY_PACKAGE_MAP: dict[int, str] = {
    9:  "cupy-cuda9",          # çok eski, nadiren karşılaşılır
    10: "cupy-cuda10",
    11: "cupy-cuda11x[ctk]",
    12: "cupy-cuda12x[ctk]",
    13: "cupy-cuda13x[ctk]",
    # Gelecek sürümler buraya eklenebilir; bilinmeyen sürüm → None → atla
}

# ---------------------------------------------------------------------------
# Yardımcı işlevler
# ---------------------------------------------------------------------------

def _ensure_deps_on_path() -> None:
    """_deps/ klasörünü sys.path'e ekler (zaten eklenmemişse)."""
    if _DEPS not in sys.path:
        sys.path.insert(0, _DEPS)


def _is_importable(import_name: str) -> bool:
    """Modülün içe aktarılabilir olup olmadığını kontrol eder."""
    try:
        importlib.import_module(import_name)
        return True
    except ImportError:
        return False


def _pip_install(packages: list[str]) -> None:
    """
    Verilen paketleri _deps/ klasörüne yükler.
    Hata oluşursa RuntimeError fırlatır.
    """
    os.makedirs(_DEPS, exist_ok=True)
    cmd = [
        sys.executable, "-m", "pip", "install",
        "--target", _DEPS,
        "--quiet",
        "--disable-pip-version-check",
        *packages,
    ]
    print(f"[bootstrap] Yükleniyor: {', '.join(packages)}", flush=True)
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(
            f"[bootstrap] Paket yüklenemedi: {packages}\n{result.stderr}"
        )
    # Yeni yüklenen modülleri Python'un modül önbelleğine ekle
    importlib.invalidate_caches()
    print("[bootstrap] Yükleme tamamlandı.", flush=True)


# ---------------------------------------------------------------------------
# Çekirdek paket kurulumu
# ---------------------------------------------------------------------------

def _install_core() -> None:
    """
    numpy ve opencv-python'ı kontrol eder; eksikse yükler.
    """
    missing = [
        pkg
        for pkg, import_name in _CORE_PACKAGES
        if not _is_importable(import_name)
    ]
    if missing:
        _pip_install(missing)


# ---------------------------------------------------------------------------
# CUDA sürümü tespiti
# ---------------------------------------------------------------------------

def _detect_cuda_major() -> int | None:
    """
    nvidia-smi çıktısından CUDA major sürümünü döndürür.
    GPU veya sürücü yoksa None döndürür; asla hata fırlatmaz.

    nvidia-smi çıktısı örneği (header bölümü):
        +-------------------------...+
        | NVIDIA-SMI 576.52        Driver Version: 576.52   CUDA Version: 13.0 |
    """
    try:
        result = subprocess.run(
            ["nvidia-smi"],
            capture_output=True,
            text=True,
            timeout=10,
        )
        if result.returncode != 0:
            return None

        for line in result.stdout.splitlines():
            if "CUDA Version:" in line:
                # "CUDA Version: 13.0" → "13.0" → 13
                after_colon = line.split("CUDA Version:")[-1].strip()
                version_str = after_colon.split()[0]   # "13.0" veya "13.0|"
                version_str = version_str.rstrip("|").strip()
                major = int(version_str.split(".")[0])
                return major

    except (FileNotFoundError, subprocess.TimeoutExpired, ValueError, IndexError):
        # nvidia-smi yok ya da çıktı beklenmedik formatta
        pass

    return None


# ---------------------------------------------------------------------------
# CuPy kurulumu
# ---------------------------------------------------------------------------

def _install_cupy() -> None:
    """
    GPU'yu ve CUDA sürümünü tespit eder; uygun cupy paketini yükler.

    Kurulum yapılmaz:
      - nvidia-smi bulunamazsa (GPU/sürücü yok)
      - CUDA sürümü haritada yoksa (çok eski ya da henüz tanımsız)
      - cupy zaten içe aktarılabiliyorsa
    """
    # Cupy zaten yüklüyse bir şey yapma
    if _is_importable("cupy"):
        return

    cuda_major = _detect_cuda_major()

    if cuda_major is None:
        print(
            "[bootstrap] NVIDIA GPU/sürücüsü bulunamadı — "
            "CuPy kurulmayacak. GPU desteği devre dışı.",
            flush=True,
        )
        return

    cupy_pkg = _CUPY_PACKAGE_MAP.get(cuda_major)

    if cupy_pkg is None:
        print(
            f"[bootstrap] CUDA {cuda_major} için bilinen bir CuPy paketi yok. "
            f"Bilinen sürümler: {sorted(_CUPY_PACKAGE_MAP.keys())}. "
            "CuPy kurulmayacak — CPU modu kullanılacak.",
            flush=True,
        )
        return

    print(
        f"[bootstrap] CUDA {cuda_major} tespit edildi → {cupy_pkg} yükleniyor...",
        flush=True,
    )
    try:
        _pip_install([cupy_pkg])
    except RuntimeError as exc:
        # CuPy isteğe bağlıdır — kurulum başarısız olsa bile devam et
        print(
            f"[bootstrap] CuPy kurulumu başarısız (GPU desteği devre dışı):\n{exc}",
            flush=True,
        )


# ---------------------------------------------------------------------------
# Ana giriş noktası — modül yüklendiğinde otomatik çalışır
# ---------------------------------------------------------------------------

def ensure() -> None:
    """
    Tüm bağımlılıkların kurulu olmasını sağlar.
    Modül import edilir edilmez çağrılır.
    """
    _ensure_deps_on_path()
    _install_core()
    _install_cupy()


ensure()
