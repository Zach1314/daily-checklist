# Daily Checklist

A lightweight Windows desktop widget that shows your recurring daily work checklist in a translucent floating window.

Double-click `DailyChecklist.exe` in this folder and the widget appears in the top-right corner of your primary screen. Drag the title bar to move it; the position is restored the next time you open it.

- Click **编辑清单** (Edit List), type one recurring task per line, then click **保存清单** (Save List). While editing you can also press Ctrl+S to save or Esc to cancel.
- Click a task to check it off; click again to uncheck. Checked states persist if you close and reopen the widget on the same day.
- At local midnight all items reset to unchecked. If the PC was off or asleep, the reset happens on the next launch or wake.
- Drag the widget where you like, then click **⋯** in the top-right corner and choose **锁定位置** (Lock Position). Once locked it can no longer be dragged, and the position and lock state are restored after a restart. Click **锁定位置** again to unlock.
- The **⋯** menu also adjusts background opacity, toggles always-on-top, and closes the widget. If the window gets buried, double-click the program again to bring it back.

The program is portable and runs as a normal desktop window. Always-on-top is off by default. The checklist and settings are stored in `%LOCALAPPDATA%\DailyChecklistWidget\checklist.json`.

To rebuild from source, run `./source/build.ps1` in PowerShell 7. The build uses the .NET Framework tools that ship with Windows.
