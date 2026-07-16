"""
test_gui.py
===========
Tkinter tabanlı test arayüzü – grayscale_clr.GrayscaleProcessor

Çalıştırma:
    python test_gui.py          (venv aktifken)
"""

import sys
import os
import threading
import tkinter as tk
from tkinter import ttk, filedialog, messagebox
from PIL import Image, ImageTk
import numpy as np
# pyrefly: ignore [missing-import]
import cv2

# grayscale_clr'ı aynı dizinden yükle
sys.path.insert(0, os.path.dirname(__file__))
from grayscale_clr import GrayscaleProcessor

# ─── Renk paleti ────────────────────────────────────────────────────────────
BG        = "#0f1117"
SURFACE   = "#1a1d27"
CARD      = "#22263a"
ACCENT    = "#7c6fcd"
ACCENT2   = "#56cfb2"
TEXT      = "#e8eaf6"
TEXT_DIM  = "#7b82a4"
RED       = "#ef5350"
GREEN     = "#66bb6a"
BORDER    = "#2e3352"

FONT_TITLE  = ("Segoe UI", 16, "bold")
FONT_HEAD   = ("Segoe UI", 11, "bold")
FONT_BODY   = ("Segoe UI", 10)
FONT_MONO   = ("Consolas", 9)
FONT_SMALL  = ("Segoe UI", 8)

# ─── Yardımcı widget'lar ────────────────────────────────────────────────────

def styled_frame(parent, **kw):
    kw.setdefault("bg", SURFACE)
    kw.setdefault("relief", "flat")
    return tk.Frame(parent, **kw)

def label(parent, text, font=FONT_BODY, fg=TEXT, **kw):
    kw.setdefault("bg", parent["bg"] if hasattr(parent, "__getitem__") else SURFACE)
    return tk.Label(parent, text=text, font=font, fg=fg, **kw)

def card(parent, **kw):
    kw.setdefault("bg", CARD)
    kw.setdefault("relief", "flat")
    kw.setdefault("bd", 0)
    f = tk.Frame(parent, **kw)
    return f

def accent_button(parent, text, command, color=ACCENT, **kw):
    btn = tk.Button(
        parent, text=text, command=command,
        bg=color, fg="white", font=FONT_BODY,
        relief="flat", bd=0, padx=14, pady=7,
        activebackground=color, activeforeground="white",
        cursor="hand2", **kw
    )
    def on_enter(e): btn.config(bg=_lighten(color))
    def on_leave(e): btn.config(bg=color)
    btn.bind("<Enter>", on_enter)
    btn.bind("<Leave>", on_leave)
    return btn

def _lighten(hex_color):
    # Expand 3-digit shorthand (#RGB → #RRGGBB) before slicing
    h = hex_color.lstrip("#")
    if len(h) == 3:
        h = h[0]*2 + h[1]*2 + h[2]*2
    r = min(255, int(h[0:2], 16) + 30)
    g = min(255, int(h[2:4], 16) + 30)
    b = min(255, int(h[4:6], 16) + 30)
    return f"#{r:02x}{g:02x}{b:02x}"

def styled_entry(parent, width=22, **kw):
    e = tk.Entry(
        parent, width=width, font=FONT_BODY,
        bg="#2b2f45", fg=TEXT, insertbackground=TEXT,
        relief="flat", bd=0, highlightthickness=1,
        highlightcolor=ACCENT, highlightbackground=BORDER,
        **kw
    )
    return e

def styled_spinbox(parent, from_, to, textvariable, width=8):
    s = tk.Spinbox(
        parent, from_=from_, to=to, textvariable=textvariable,
        width=width, font=FONT_BODY,
        bg="#2b2f45", fg=TEXT, insertbackground=TEXT,
        buttonbackground="#2b2f45", relief="flat",
        highlightthickness=1, highlightcolor=ACCENT,
        highlightbackground=BORDER
    )
    return s

# ─── Ana uygulama ─────────────────────────────────────────────────────────

class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("GrayFocus — GrayscaleProcessor Test")
        self.geometry("1200x820")
        self.minsize(900, 640)
        self.configure(bg=BG)

        self.processor  = GrayscaleProcessor()
        self.session_id = "test_session"
        self.session_active = False

        # ─ Seçilen dosyalar
        self.selected_files: list[str] = []
        self.preview_ids:    list[str] = []

        self._build_ui()

    # ── UI inşası ─────────────────────────────────────────────────────────

    def _build_ui(self):
        # Başlık çubuğu
        header = styled_frame(self, bg=SURFACE, height=56)
        header.pack(fill="x", side="top")
        header.pack_propagate(False)
        label(header, "⬡  GrayFocus Processor  —  Test GUI",
              font=FONT_TITLE, fg=ACCENT, bg=SURFACE).pack(side="left", padx=20, pady=10)
        label(header, "grayscale_clr.py",
              font=FONT_SMALL, fg=TEXT_DIM, bg=SURFACE).pack(side="right", padx=20)

        # İki sütunlu ana gövde
        body = styled_frame(self, bg=BG)
        body.pack(fill="both", expand=True, padx=12, pady=(8, 12))
        body.columnconfigure(0, weight=3, minsize=360)
        body.columnconfigure(1, weight=4, minsize=400)
        body.rowconfigure(0, weight=1)

        left  = styled_frame(body, bg=BG)
        right = styled_frame(body, bg=BG)
        left.grid(row=0, column=0, sticky="nsew", padx=(0, 6))
        right.grid(row=0, column=1, sticky="nsew")

        self._build_left(left)
        self._build_right(right)

    def _build_left(self, parent):
        # ── Oturum ayarları ──────────────────────────────────────────────
        c = card(parent)
        c.pack(fill="x", pady=(0, 8))
        self._section_header(c, "⚙  Oturum Ayarları")

        grid = styled_frame(c, bg=CARD)
        grid.pack(fill="x", padx=14, pady=(4, 14))

        self.var_session = tk.StringVar(value="test_session")
        self.var_min     = tk.IntVar(value=100)
        self.var_max     = tk.IntVar(value=200)
        self.var_total   = tk.IntVar(value=0)
        self.var_prev    = tk.IntVar(value=5)

        rows = [
            ("Session ID",       self.var_session, "entry"),
            ("Min Değer (0-65535)", self.var_min, "spin"),
            ("Max Değer (0-65535)", self.var_max, "spin"),
            ("Toplam Görüntü (0=bilinmiyor)", self.var_total, "spin"),
            ("Preview Sayısı",   self.var_prev, "spin"),
        ]
        for i, (lbl, var, kind) in enumerate(rows):
            label(grid, lbl, fg=TEXT_DIM, bg=CARD).grid(row=i, column=0, sticky="w", pady=3, padx=4)
            if kind == "entry":
                w = styled_entry(grid, textvariable=var, width=18)
            else:
                w = styled_spinbox(grid, 0, 65535, var)
            w.grid(row=i, column=1, sticky="e", pady=3, padx=4)

        btn_row = styled_frame(c, bg=CARD)
        btn_row.pack(fill="x", padx=14, pady=(0, 14))
        self.btn_start   = accent_button(btn_row, "▶  Oturum Başlat",   self._start_session, color=ACCENT)
        self.btn_cleanup = accent_button(btn_row, "✕  Oturumu Kapat",   self._cleanup_session, color=RED)
        self.btn_start.pack(side="left", padx=(0, 8))
        self.btn_cleanup.pack(side="left")
        self._update_session_state()

        # ── Dosya seçimi ──────────────────────────────────────────────────
        c2 = card(parent)
        c2.pack(fill="x", pady=(0, 8))
        self._section_header(c2, "Görüntü Dosyaları")

        btn_row2 = styled_frame(c2, bg=CARD)
        btn_row2.pack(fill="x", padx=14, pady=(4, 6))
        accent_button(btn_row2, "Dosya Seç…", self._pick_files).pack(side="left", padx=(0, 8))
        accent_button(btn_row2, "Temizle",    self._clear_files, color="#555").pack(side="left", padx=(0, 8))
        accent_button(btn_row2, "Örnek Bölge Seç", self._select_sample, color="#5c6bc0").pack(side="left")

        list_frame = styled_frame(c2, bg=CARD)
        list_frame.pack(fill="x", padx=14, pady=(0, 12))
        sb = tk.Scrollbar(list_frame, orient="vertical", bg=CARD, troughcolor=CARD)
        self.file_listbox = tk.Listbox(
            list_frame, height=6, font=FONT_MONO,
            bg="#1e2236", fg=TEXT, selectbackground=ACCENT,
            relief="flat", bd=0, yscrollcommand=sb.set
        )
        sb.config(command=self.file_listbox.yview)
        self.file_listbox.pack(side="left", fill="both", expand=True)
        sb.pack(side="right", fill="y")

        # ── İşleme butonu ─────────────────────────────────────────────────
        c3 = card(parent)
        c3.pack(fill="x", pady=(0, 8))
        self._section_header(c3, "▶  İşleme")
        run_row = styled_frame(c3, bg=CARD)
        run_row.pack(fill="x", padx=14, pady=(4, 14))
        self.btn_run = accent_button(run_row, "Seçili Dosyaları İşle",
                                     self._run_processing, color=ACCENT2)
        self.btn_run.pack(side="left")

        # İlerleme çubuğu
        self.progress_var = tk.DoubleVar(value=0)
        style = ttk.Style()
        style.theme_use("clam")
        style.configure("custom.Horizontal.TProgressbar",
                         troughcolor=SURFACE, background=ACCENT2,
                         bordercolor=SURFACE, lightcolor=ACCENT2, darkcolor=ACCENT2)
        self.progress_bar = ttk.Progressbar(
            c3, variable=self.progress_var, maximum=100,
            style="custom.Horizontal.TProgressbar", length=300
        )
        self.progress_bar.pack(fill="x", padx=14, pady=(0, 14))

        # ── Oturum özeti ──────────────────────────────────────────────────
        c4 = card(parent)
        c4.pack(fill="x", pady=(0, 8))
        self._section_header(c4, "Oturum Özeti")
        self.btn_summary = accent_button(c4, "Özeti Getir", self._get_summary, color="#555")
        self.btn_summary.pack(anchor="w", padx=14, pady=(4, 8))
        self.summary_text = tk.Text(
            c4, height=5, font=FONT_MONO,
            bg="#1e2236", fg=ACCENT2, relief="flat", bd=0,
            state="disabled"
        )
        self.summary_text.pack(fill="x", padx=14, pady=(0, 12))

    def _build_right(self, parent):
        # ── Log alanı ─────────────────────────────────────────────────────
        log_card = card(parent)
        log_card.pack(fill="both", expand=True, pady=(0, 8))
        hdr = styled_frame(log_card, bg=CARD)
        hdr.pack(fill="x")
        self._section_header(hdr, "İşlem Günlüğü")
        accent_button(hdr, "Temizle", self._clear_log, color="#555").pack(side="right", padx=14, pady=6)

        log_frame = styled_frame(log_card, bg="#111522")
        log_frame.pack(fill="both", expand=True, padx=14, pady=(0, 14))
        vsb = tk.Scrollbar(log_frame, orient="vertical")
        self.log_text = tk.Text(
            log_frame, font=FONT_MONO, bg="#111522", fg=TEXT,
            relief="flat", bd=0, wrap="word",
            yscrollcommand=vsb.set
        )
        vsb.config(command=self.log_text.yview)
        self.log_text.pack(side="left", fill="both", expand=True, padx=6, pady=6)
        vsb.pack(side="right", fill="y")
        # Renkli etiketler
        self.log_text.tag_config("progress",   foreground=TEXT_DIM)
        self.log_text.tag_config("completed",  foreground=GREEN)
        self.log_text.tag_config("error",      foreground=RED)
        self.log_text.tag_config("info",       foreground=ACCENT)
        self.log_text.tag_config("warn",       foreground="#ffb74d")

        # ── Önizleme galerisi ─────────────────────────────────────────────
        prev_card = card(parent)
        prev_card.pack(fill="x", pady=(0, 0))
        self._section_header(prev_card, "Önizleme Galerisi")

        nav_row = styled_frame(prev_card, bg=CARD)
        nav_row.pack(fill="x", padx=14, pady=(4, 6))
        accent_button(nav_row, "◀", self._prev_image, color="#555").pack(side="left", padx=(0, 4))
        accent_button(nav_row, "▶", self._next_image, color="#555").pack(side="left")
        self.lbl_preview_id = label(nav_row, "—", fg=TEXT_DIM, bg=CARD)
        self.lbl_preview_id.pack(side="left", padx=12)
        self.btn_load_prev = accent_button(nav_row, "Önizlemeleri Yükle",
                                           self._load_previews, color=ACCENT)
        self.btn_load_prev.pack(side="right")

        preview_bg = styled_frame(prev_card, bg="#0a0c14", height=220)
        preview_bg.pack(fill="x", padx=14, pady=(0, 14))
        preview_bg.pack_propagate(False)
        self.preview_label = tk.Label(preview_bg, bg="#0a0c14", text="Önizleme yok",
                                      fg=TEXT_DIM, font=FONT_BODY)
        self.preview_label.pack(expand=True)

        self._preview_index = 0
        self._preview_images: list[ImageTk.PhotoImage] = []

    # ── Yardımcı ─────────────────────────────────────────────────────────

    def _section_header(self, parent, text):
        f = styled_frame(parent, bg=parent["bg"], height=36)
        f.pack(fill="x")
        f.pack_propagate(False)
        label(f, text, font=FONT_HEAD, fg=TEXT, bg=parent["bg"]).pack(
            side="left", padx=14, pady=8)
        tk.Frame(f, bg=BORDER, height=1).pack(side="bottom", fill="x")

    def _log(self, msg: str, tag: str = "info"):
        self.log_text.configure(state="normal")
        self.log_text.insert("end", msg + "\n", tag)
        self.log_text.see("end")
        self.log_text.configure(state="disabled")

    def _clear_log(self):
        self.log_text.configure(state="normal")
        self.log_text.delete("1.0", "end")
        self.log_text.configure(state="disabled")

    def _update_session_state(self):
        if self.session_active:
            self.btn_start.config(state="disabled", bg="#555")
            self.btn_cleanup.config(state="normal", bg=RED)
        else:
            self.btn_start.config(state="normal", bg=ACCENT)
            self.btn_cleanup.config(state="disabled", bg="#555")

    # ── Oturum işlemleri ──────────────────────────────────────────────────

    def _start_session(self):
        self.session_id = self.var_session.get().strip() or "test_session"
        try:
            self.processor.start_session(
                self.session_id,
                min_val=self.var_min.get(),
                max_val=self.var_max.get(),
                total_expected_images=self.var_total.get(),
                preview_count=self.var_prev.get(),
            )
            self.session_active = True
            self.preview_ids.clear()
            self._preview_images.clear()
            self._update_session_state()
            self._log(f"✔ Oturum başlatıldı: '{self.session_id}'  "
                      f"min={self.var_min.get()} max={self.var_max.get()}", "completed")
        except Exception as ex:
            self._log(f"✘ Oturum başlatılamadı: {ex}", "error")

    def _cleanup_session(self):
        try:
            self.processor.cleanup_session(self.session_id)
            self.session_active = False
            self.preview_ids.clear()
            self._preview_images.clear()
            self.progress_var.set(0)
            self._update_session_state()
            self._log(f"✔ Oturum kapatıldı: '{self.session_id}'", "warn")
        except Exception as ex:
            self._log(f"✘ Cleanup hatası: {ex}", "error")

    # ── Dosya seçimi ──────────────────────────────────────────────────────

    def _pick_files(self):
        files = filedialog.askopenfilenames(
            title="Görüntü dosyalarını seçin",
            filetypes=[("Görüntüler", "*.tif *.tiff *.png *.jpg *.bmp"), ("Tümü", "*.*")]
        )
        for f in files:
            if f not in self.selected_files:
                self.selected_files.append(f)
                self.file_listbox.insert("end", os.path.basename(f))

    def _clear_files(self):
        self.selected_files.clear()
        self.file_listbox.delete(0, "end")

    # ── Örnek bölge seçimi ────────────────────────────────────────────────

    def _select_sample(self):
        """
        İlk seçili dosyayı saf Tkinter modal penceresinde açar.
        Kullanıcı canvas üzerine fare ile dikdörtgen çizer; 'Onayla'ya
        basıldığında seçilen bölgenin ham piksel min/max değerleri
        (16-bit dahil) spinbox'lara yazılır.
        cv2 GUI hiç kullanılmaz → Qt/thread sorunu olmaz.
        """
        if not self.selected_files:
            self._log("[Uyarı]: Önce dosya seçin.", "warn")
            return

        path = self.selected_files[0]

        # ── 1. Ham görüntüyü oku (16-bit korunur) ──────────────────────
        img_orig = cv2.imread(path, cv2.IMREAD_UNCHANGED)
        if img_orig is None:
            self._log(f"[Hata]: Görüntü okunamadı → {os.path.basename(path)}", "error")
            return

        # ── 2. Görüntüleme için 8-bit'e normalize et ───────────────────
        if img_orig.dtype == np.uint16:
            img_8bit = (img_orig >> 8).astype(np.uint8)
        elif img_orig.dtype != np.uint8:
            img_8bit = cv2.normalize(img_orig, None, 0, 255, cv2.NORM_MINMAX).astype(np.uint8)
        else:
            img_8bit = img_orig.copy()

        if len(img_8bit.shape) == 2:           # grayscale → RGB
            pil_disp = Image.fromarray(img_8bit)
        else:
            pil_disp = Image.fromarray(cv2.cvtColor(img_8bit, cv2.COLOR_BGR2RGB))

        # ── 3. Ekrana sığacak şekilde küçült ──────────────────────────
        MAX_W, MAX_H = 960, 680
        pil_disp.thumbnail((MAX_W, MAX_H), Image.LANCZOS)
        disp_w, disp_h = pil_disp.size
        orig_h, orig_w = img_orig.shape[:2]
        scale_x = orig_w / disp_w
        scale_y = orig_h / disp_h

        # ── 4. Modal Toplevel penceresi ────────────────────────────────
        win = tk.Toplevel(self)
        win.title(f"Örnek Bölge Seç  —  {os.path.basename(path)}")
        win.configure(bg=BG)
        win.resizable(False, False)
        win.grab_set()           # modal: ana pencereyi kilitle

        info = tk.Label(
            win,
            text="Fare ile dikdörtgen çizin, ardından 'Onayla' butonuna tıklayın.",
            font=FONT_SMALL, fg=TEXT_DIM, bg=BG
        )
        info.pack(pady=(8, 2))

        tk_img = ImageTk.PhotoImage(pil_disp)
        canvas = tk.Canvas(
            win, width=disp_w, height=disp_h,
            cursor="crosshair", bg="#000",
            highlightthickness=1, highlightbackground=BORDER
        )
        canvas.pack(padx=10, pady=6)
        canvas.create_image(0, 0, anchor="nw", image=tk_img)
        canvas.image = tk_img   # referansı koru

        rect_id   = [None]
        drag_start = [0, 0]
        roi_coords = [None]     # (x0, y0, x1, y1) — display px

        coord_lbl = tk.Label(win, text="", font=FONT_MONO, fg=TEXT_DIM, bg=BG)
        coord_lbl.pack(pady=(0, 4))

        def _press(e):
            drag_start[0], drag_start[1] = e.x, e.y
            if rect_id[0]:
                canvas.delete(rect_id[0])

        def _drag(e):
            if rect_id[0]:
                canvas.delete(rect_id[0])
            rect_id[0] = canvas.create_rectangle(
                drag_start[0], drag_start[1], e.x, e.y,
                outline="#00e5ff", width=2, dash=(4, 2)
            )
            w = abs(e.x - drag_start[0])
            h = abs(e.y - drag_start[1])
            coord_lbl.config(
                text=f"Görüntüleme: {w}×{h} px  |  "
                     f"Orijinal: {int(w*scale_x)}×{int(h*scale_y)} px"
            )

        def _release(e):
            x0 = min(drag_start[0], e.x)
            y0 = min(drag_start[1], e.y)
            x1 = max(drag_start[0], e.x)
            y1 = max(drag_start[1], e.y)
            roi_coords[0] = (x0, y0, x1, y1)

        canvas.bind("<ButtonPress-1>",   _press)
        canvas.bind("<B1-Motion>",       _drag)
        canvas.bind("<ButtonRelease-1>", _release)

        # ── 5. Onayla / İptal ─────────────────────────────────────────
        def _confirm():
            roi = roi_coords[0]
            if roi is None or (roi[2]-roi[0]) < 2 or (roi[3]-roi[1]) < 2:
                self._log("[Uyarı]: Bölge seçilmedi. Min/Max değişmedi.", "warn")
                win.destroy()
                return

            x0, y0, x1, y1 = roi
            # Görüntüleme koordinatlarını orijinal piksel koordinatlarına dönüştür
            ox0, oy0 = int(x0 * scale_x), int(y0 * scale_y)
            ox1, oy1 = int(x1 * scale_x), int(y1 * scale_y)

            patch   = img_orig[oy0:oy1, ox0:ox1]
            min_val = int(np.min(patch))
            max_val = int(np.max(patch))

            self.var_min.set(min_val)
            self.var_max.set(max_val)
            self._log(
                f"Örnekleme tamamlandi → min={min_val}, max={max_val}  "
                f"(orijinal: x={ox0} y={oy0}  {ox1-ox0}x{oy1-oy0} px)",
                "completed"
            )
            win.destroy()

        btn_row = tk.Frame(win, bg=BG)
        btn_row.pack(pady=(2, 10))
        accent_button(btn_row, "Onayla", _confirm, color=ACCENT2).pack(side="left", padx=8)
        accent_button(btn_row, "Iptal",  win.destroy, color="#555").pack(side="left")

        win.wait_window()   # ana döngüyü bloklama, modal olarak bekle

    # ── İşleme ────────────────────────────────────────────────────────────

    def _run_processing(self):
        if not self.session_active:
            messagebox.showwarning("Uyarı", "Önce bir oturum başlatın.")
            return
        if not self.selected_files:
            messagebox.showwarning("Uyarı", "İşlenecek dosya seçmediniz.")
            return

        # If total_expected_images was left at 0 (unknown), the algorithm uses
        # preview_count as step_size, which means it saves a preview every
        # preview_count-th image – NOT preview_count total previews.
        # Fix: restart the session with the real file count so the step_size
        # is computed correctly and exactly preview_count previews are produced.
        if self.var_total.get() == 0:
            try:
                self.processor.cleanup_session(self.session_id)
            except Exception:
                pass
            actual_total = len(self.selected_files)
            self.processor.start_session(
                self.session_id,
                min_val=self.var_min.get(),
                max_val=self.var_max.get(),
                total_expected_images=actual_total,
                preview_count=self.var_prev.get(),
            )
            self.preview_ids.clear()
            self._preview_images.clear()
            self.after(0, self._log,
                       f"ℹ 'Toplam Görüntü' = 0 olduğundan oturum "
                       f"{actual_total} dosya için yeniden başlatıldı "
                       f"(step_size = {actual_total // self.var_prev.get()}).",
                       "warn")

        self.btn_run.config(state="disabled", text="⏳ İşleniyor…")
        threading.Thread(target=self._worker, daemon=True).start()

    def _worker(self):
        total = len(self.selected_files)
        for i, path in enumerate(self.selected_files):
            self._log(f"\n── [{i+1}/{total}] {os.path.basename(path)}", "info")
            try:
                def cb(payload, idx=i+1):
                    status = payload.get("status", "")
                    msg    = payload.get("message", "")
                    step   = payload.get("step")
                    if status == "progress":
                        tag = "progress"
                        line = f"  [{step}/4] {msg}"
                    elif status == "completed":
                        tag = "completed"
                        line = (f"  ✔ Tamamlandı – "
                                f"piksel={payload.get('image_pixels_in_range',0)}, "
                                f"genel={payload.get('global_total_pixels',0)}, "
                                f"önizleme={payload.get('saved_preview_id')}")
                        pid = payload.get("saved_preview_id")
                        if pid and pid not in self.preview_ids:
                            self.preview_ids.append(pid)
                    else:
                        tag = "warn"
                        line = f"  {msg}"
                    self.after(0, self._log, line, tag)

                result = self.processor.process_image(self.session_id, path, cb)
                pct = ((i + 1) / total) * 100
                self.after(0, self.progress_var.set, pct)
            except Exception as ex:
                self.after(0, self._log, f"  ✘ Hata: {ex}", "error")

        self.after(0, self._on_processing_done)

    def _on_processing_done(self):
        self.btn_run.config(state="normal", text="🚀  Seçili Dosyaları İşle")
        self._log("\n✔ Tüm dosyalar işlendi.", "completed")

    # ── Özet ──────────────────────────────────────────────────────────────

    def _get_summary(self):
        if not self.session_active:
            messagebox.showwarning("Uyarı", "Aktif oturum yok.")
            return
        try:
            r = self.processor.get_session_results(self.session_id)
            text = (
                f"session_id             : {r['session_id']}\n"
                f"total_images_processed : {r['total_images_processed']}\n"
                f"global_total_pixels    : {r['global_total_pixels']}\n"
                f"periodic_previews      : {r['periodic_previews']}"
            )
            self.summary_text.configure(state="normal")
            self.summary_text.delete("1.0", "end")
            self.summary_text.insert("end", text)
            self.summary_text.configure(state="disabled")
        except Exception as ex:
            self._log(f"✘ Özet alınamadı: {ex}", "error")

    # ── Önizleme galerisi ─────────────────────────────────────────────────

    def _load_previews(self):
        if not self.session_active:
            messagebox.showwarning("Uyarı", "Aktif oturum yok.")
            return
        self._preview_images.clear()
        try:
            # Always fetch the authoritative list from the session so that
            # images evicted by the cluster-bucket algorithm are never requested.
            current_ids = self.processor.get_session_results(
                self.session_id
            )["periodic_previews"]
        except Exception as ex:
            self._log(f"✘ Oturum sonuçları alınamadı: {ex}", "error")
            return

        for pid in current_ids:
            try:
                arr = self.processor.get_image(self.session_id, pid)
                rgb = cv2.cvtColor(arr, cv2.COLOR_BGR2RGB)
                pil = Image.fromarray(rgb)
                pil.thumbnail((700, 210))
                self._preview_images.append((pid, ImageTk.PhotoImage(pil)))
            except Exception as ex:
                self._log(f"✘ get_image hatası ({pid}): {ex}", "error")

        if self._preview_images:
            self._preview_index = 0
            self._show_preview()
            self._log(f"✔ {len(self._preview_images)} önizleme yüklendi.", "completed")
        else:
            self._log("ℹ Gösterilecek önizleme yok.", "warn")

    def _show_preview(self):
        if not self._preview_images:
            return
        idx = self._preview_index
        pid, photo = self._preview_images[idx]
        self.preview_label.config(image=photo, text="")
        self.preview_label.image = photo
        self.lbl_preview_id.config(
            text=f"{idx+1}/{len(self._preview_images)}  –  {pid}"
        )

    def _prev_image(self):
        if self._preview_images:
            self._preview_index = (self._preview_index - 1) % len(self._preview_images)
            self._show_preview()

    def _next_image(self):
        if self._preview_images:
            self._preview_index = (self._preview_index + 1) % len(self._preview_images)
            self._show_preview()

    def _open_preview_zoom(self):
        """Gecërli önizlemeyi tam çözünürlükte ayrı bir Toplevel penceresinde gösterir."""
        if not self._preview_images:
            return

        pid, _ = self._preview_images[self._preview_index]

        # Orijinal numpy dizisini çek (küçültülmemiş tam çözünürlük)
        try:
            arr = self.processor.get_image(self.session_id, pid)
        except Exception as ex:
            self._log(f"[Hata]: Buyutme için goruntu alinamadi: {ex}", "error")
            return

        rgb = cv2.cvtColor(arr, cv2.COLOR_BGR2RGB)
        pil_full = Image.fromarray(rgb)
        img_w, img_h = pil_full.size

        # Ekrana sığacak şekilde ölçekle (ama gerçek çözünürlükü asm a)
        screen_w = self.winfo_screenwidth()  - 80
        screen_h = self.winfo_screenheight() - 120
        scale = min(1.0, screen_w / img_w, screen_h / img_h)
        disp_w = int(img_w * scale)
        disp_h = int(img_h * scale)
        pil_disp = pil_full.resize((disp_w, disp_h), Image.LANCZOS)

        # ── Pencere ──────────────────────────────────────────────────────
        win = tk.Toplevel(self)
        win.title(f"{pid}  ({img_w}×{img_h} px  |  ekran: {disp_w}×{disp_h})")
        win.configure(bg=BG)

        # Scrollable canvas (büyük görüntüler için)
        frame = tk.Frame(win, bg=BG)
        frame.pack(fill="both", expand=True)

        vsb = tk.Scrollbar(frame, orient="vertical",   bg=BG, troughcolor=SURFACE)
        hsb = tk.Scrollbar(frame, orient="horizontal", bg=BG, troughcolor=SURFACE)
        canvas = tk.Canvas(
            frame,
            width=min(disp_w, screen_w),
            height=min(disp_h, screen_h),
            bg="#0a0c14",
            yscrollcommand=vsb.set,
            xscrollcommand=hsb.set,
            highlightthickness=0,
        )
        vsb.config(command=canvas.yview)
        hsb.config(command=canvas.xview)

        canvas.grid(row=0, column=0, sticky="nsew")
        vsb.grid(row=0, column=1, sticky="ns")
        hsb.grid(row=1, column=0, sticky="ew")
        frame.rowconfigure(0, weight=1)
        frame.columnconfigure(0, weight=1)

        tk_img = ImageTk.PhotoImage(pil_disp)
        canvas.create_image(0, 0, anchor="nw", image=tk_img)
        canvas.image = tk_img   # referansi koru
        canvas.configure(scrollregion=(0, 0, disp_w, disp_h))

        # Fare tekerlegi ile kaydirma
        canvas.bind("<MouseWheel>",
                    lambda e: canvas.yview_scroll(int(-1 * (e.delta / 120)), "units"))
        canvas.bind("<Button-4>",
                    lambda e: canvas.yview_scroll(-1, "units"))
        canvas.bind("<Button-5>",
                    lambda e: canvas.yview_scroll( 1, "units"))

        # Meta bilgisi
        info_bar = tk.Frame(win, bg=SURFACE, height=28)
        info_bar.pack(fill="x", side="bottom")
        info_bar.pack_propagate(False)
        tk.Label(
            info_bar,
            text=f"  {pid}   |   {img_w}×{img_h} px   |   Cift tikla: kapat",
            font=FONT_SMALL, fg=TEXT_DIM, bg=SURFACE, anchor="w"
        ).pack(fill="x", padx=8, pady=4)

        win.bind("<Double-Button-1>", lambda e: win.destroy())
        win.bind("<Escape>",          lambda e: win.destroy())


# ─── Giriş noktası ─────────────────────────────────────────────────────────

if __name__ == "__main__":
    try:
        from PIL import Image  # noqa: F401 – kontrol
    except ImportError:
        print("Pillow bulunamadı. Lütfen bu komut ile kurunuz: pip install Pillow")
        sys.exit(1)

    app = App()
    app.mainloop()
