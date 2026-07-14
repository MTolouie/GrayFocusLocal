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
    r = min(255, int(hex_color[1:3], 16) + 30)
    g = min(255, int(hex_color[3:5], 16) + 30)
    b = min(255, int(hex_color[5:7], 16) + 30)
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
        self._section_header(c2, "📂  Görüntü Dosyaları")

        btn_row2 = styled_frame(c2, bg=CARD)
        btn_row2.pack(fill="x", padx=14, pady=(4, 6))
        accent_button(btn_row2, "Dosya Seç…", self._pick_files).pack(side="left", padx=(0, 8))
        accent_button(btn_row2, "Temizle",    self._clear_files, color="#555").pack(side="left")

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
        self.btn_run = accent_button(run_row, "🚀  Seçili Dosyaları İşle",
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
        self._section_header(c4, "📊  Oturum Özeti")
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
        self._section_header(hdr, "📋  İşlem Günlüğü")
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
        self._section_header(prev_card, "🖼  Önizleme Galerisi")

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
        for pid in self.preview_ids:
            try:
                arr = self.processor.get_image(self.session_id, pid)
                # BGR → RGB
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


# ─── Giriş noktası ─────────────────────────────────────────────────────────

if __name__ == "__main__":
    try:
        from PIL import Image  # noqa: F401 – kontrol
    except ImportError:
        print("Pillow bulunamadı. Lütfen kurun: pip install Pillow")
        sys.exit(1)

    app = App()
    app.mainloop()
