# Editor Window Icon

Tell your open Unity editors apart on the Windows taskbar. Each editor shows its project's
**Player ▸ Default Icon** on the taskbar, with the Unity logo as a small badge in the bottom-right corner.

Windows only. Unity 2021.3 or newer.

## Install

In Unity, open **Window ▸ Package Manager**, then **+ ▸ Install package from git URL**, and enter:

```
https://github.com/MarnixWyns/unity-editor-window-icon.git#v1.0.1
```

Or add this line to `Packages/manifest.json`:

```json
"com.biteme.editor-window-icon": "https://github.com/MarnixWyns/unity-editor-window-icon.git#v1.0.1"
```

Then set a **Default Icon** in **Project Settings ▸ Player**. The icon updates within a couple of
seconds, and again whenever you change the Default Icon.

## How it works

- The icon is the Default Icon with the Unity logo of the running editor version as a badge.
- It becomes the icon of the editor's windows: the title bar and Alt-Tab.
- Windows 11 draws a taskbar button with the icon of its app group, and all Unity editors share a
  single group. The package therefore gives each project its own group (a per-project
  AppUserModelID) with the badged icon, written to `Library/EditorWindowIcon/`. That button
  can sit apart from your other Unity editors.
- If you pin that taskbar button, it reopens this project in the same Unity version.
- Projects without a Default Icon keep Unity's own icon.

The package adds nothing to your project's assets or settings, and it's left out of builds.

## License

[CC0 1.0](LICENSE.md): public domain. Use it however you like.
