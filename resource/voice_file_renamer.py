# -*- coding: utf-8 -*-
"""
보이스 파일 정리 프로그램

기능:
1. 상위 폴더(예: usoSCtheater\\voice) 안에 있는 S001, S002 ... 같은 하위 폴더들을 스캔
2. 각 하위 폴더 안의 오디오 파일에 대해
   - 파일명의 첫 '_' 앞부분(접두부)을 추출
   - 새 파일명 = 폴더명 + 접두부 (확장자 유지)
     예) S002 폴더 안의 "001_四国めたん（ツンツン）_はぁあー.wav"
         -> "S002001.wav"
3. 미리보기 후 확인을 거쳐 실제 이름 변경 실행
"""

import os
import re
import tkinter as tk
from tkinter import ttk, filedialog, messagebox

AUDIO_EXTS = {'.wav', '.mp3', '.ogg', '.flac', '.m4a', '.aac', '.wma', '.aiff', '.opus'}
DEFAULT_DIR = r"D:\usosctheater\usoSCtheater"


class VoiceRenamerApp:
    def __init__(self, root):
        self.root = root
        root.title("보이스 파일 정리 프로그램")
        root.geometry("900x620")

        self.target_dir = tk.StringVar(value=DEFAULT_DIR)
        self.plan = []  # 미리보기 결과 저장

        self._build_ui()

    # ---------- UI 구성 ----------
    def _build_ui(self):
        top = tk.Frame(self.root, padx=10, pady=10)
        top.pack(fill='x')

        tk.Label(top, text="대상 폴더 (S0xx 폴더들이 들어있는 상위 폴더):").pack(anchor='w')

        path_frame = tk.Frame(top)
        path_frame.pack(fill='x', pady=5)
        tk.Entry(path_frame, textvariable=self.target_dir).pack(side='left', fill='x', expand=True)
        tk.Button(path_frame, text="찾아보기", command=self.browse_folder).pack(side='left', padx=5)

        btn_frame = tk.Frame(top)
        btn_frame.pack(fill='x', pady=5)
        tk.Button(btn_frame, text="미리보기", command=self.preview).pack(side='left', padx=5)
        self.exec_btn = tk.Button(btn_frame, text="실행 (이름 변경)", command=self.execute, state='disabled')
        self.exec_btn.pack(side='left', padx=5)
        tk.Button(btn_frame, text="초기화", command=self.reset).pack(side='left', padx=5)

        columns = ('folder', 'old', 'new', 'status')
        self.tree = ttk.Treeview(self.root, columns=columns, show='headings', height=18)
        self.tree.heading('folder', text='폴더')
        self.tree.heading('old', text='원본 파일명')
        self.tree.heading('new', text='변경될 파일명')
        self.tree.heading('status', text='상태')
        self.tree.column('folder', width=80, anchor='center')
        self.tree.column('old', width=380)
        self.tree.column('new', width=180)
        self.tree.column('status', width=140, anchor='center')
        self.tree.pack(fill='both', expand=True, padx=10, pady=5)

        self.tree.tag_configure('ok', foreground='black')
        self.tree.tag_configure('warn', foreground='gray')

        log_frame = tk.Frame(self.root, padx=10, pady=5)
        log_frame.pack(fill='x')
        tk.Label(log_frame, text="로그").pack(anchor='w')
        self.log_text = tk.Text(log_frame, height=7, state='disabled')
        self.log_text.pack(fill='x')

    def log(self, msg):
        self.log_text.config(state='normal')
        self.log_text.insert('end', msg + '\n')
        self.log_text.see('end')
        self.log_text.config(state='disabled')

    # ---------- 동작 ----------
    def browse_folder(self):
        initial = self.target_dir.get() or DEFAULT_DIR
        folder = filedialog.askdirectory(initialdir=initial, title="S0xx 폴더들이 들어있는 폴더 선택")
        if folder:
            self.target_dir.set(folder)

    @staticmethod
    def extract_prefix(name_no_ext):
        if '_' in name_no_ext:
            prefix = name_no_ext.split('_', 1)[0]
        else:
            prefix = name_no_ext
        return prefix.strip()

    def preview(self):
        base = self.target_dir.get().strip()
        if not base or not os.path.isdir(base):
            messagebox.showerror("오류", "올바른 폴더를 선택하세요.")
            return

        self.tree.delete(*self.tree.get_children())
        self.plan = []

        subfolders = [f for f in os.listdir(base) if os.path.isdir(os.path.join(base, f))]
        if not subfolders:
            messagebox.showwarning("알림", "선택한 폴더 안에 하위 폴더가 없습니다.")
            return

        for folder_name in subfolders:
            folder_path = os.path.join(base, folder_name)
            files = [f for f in os.listdir(folder_path) if os.path.isfile(os.path.join(folder_path, f))]
            used_targets = {}
            existing_lower = {f.lower() for f in files}

            for fname in files:
                name_no_ext, ext = os.path.splitext(fname)
                if ext.lower() not in AUDIO_EXTS:
                    continue

                # 이미 "폴더명+숫자" 형태면 처리된 것으로 간주하고 건너뜀
                if re.fullmatch(re.escape(folder_name) + r'\d+', name_no_ext):
                    self.plan.append({
                        'folder_path': folder_path, 'folder_name': folder_name,
                        'old_name': fname, 'new_name': fname, 'status': '이미 처리됨',
                    })
                    continue

                prefix = self.extract_prefix(name_no_ext)
                new_name = f"{folder_name}{prefix}{ext}"

                if new_name.lower() == fname.lower():
                    status = '변경 없음'
                elif new_name.lower() in used_targets:
                    status = '충돌(중복 대상)'
                elif new_name.lower() in existing_lower and new_name.lower() != fname.lower():
                    status = '충돌(파일 존재)'
                else:
                    status = '정상'
                    used_targets[new_name.lower()] = fname

                self.plan.append({
                    'folder_path': folder_path, 'folder_name': folder_name,
                    'old_name': fname, 'new_name': new_name, 'status': status,
                })

        if not self.plan:
            messagebox.showwarning("알림", "처리할 오디오 파일을 찾지 못했습니다.")
            return

        for item in self.plan:
            tag = 'ok' if item['status'] == '정상' else 'warn'
            self.tree.insert('', 'end',
                              values=(item['folder_name'], item['old_name'], item['new_name'], item['status']),
                              tags=(tag,))

        ok_count = sum(1 for p in self.plan if p['status'] == '정상')
        self.log(f"미리보기 완료: 총 {len(self.plan)}개 파일, 변경 대상 {ok_count}개")
        self.exec_btn.config(state='normal' if ok_count > 0 else 'disabled')

    def execute(self):
        ok_items = [p for p in self.plan if p['status'] == '정상']
        if not ok_items:
            messagebox.showinfo("알림", "변경할 파일이 없습니다.")
            return

        if not messagebox.askyesno("확인", f"{len(ok_items)}개 파일의 이름을 변경합니다. 계속할까요?"):
            return

        success, fail = 0, 0
        for item in ok_items:
            old_path = os.path.join(item['folder_path'], item['old_name'])
            new_path = os.path.join(item['folder_path'], item['new_name'])
            try:
                os.rename(old_path, new_path)
                self.log(f"[OK] {item['folder_name']}/{item['old_name']} -> {item['new_name']}")
                success += 1
            except Exception as e:
                self.log(f"[실패] {item['folder_name']}/{item['old_name']} : {e}")
                fail += 1

        messagebox.showinfo("완료", f"성공 {success}개, 실패 {fail}개")
        self.preview()  # 변경 후 목록 갱신

    def reset(self):
        self.tree.delete(*self.tree.get_children())
        self.plan = []
        self.exec_btn.config(state='disabled')
        self.log_text.config(state='normal')
        self.log_text.delete('1.0', 'end')
        self.log_text.config(state='disabled')


if __name__ == '__main__':
    root = tk.Tk()
    app = VoiceRenamerApp(root)
    root.mainloop()
