# Editor Window Icon

Tell your open Unity editors apart on the Windows taskbar and the macOS Dock. Each editor shows its
project's **Player ▸ Default Icon**, with the Unity logo as a small badge in the bottom-right corner.

Windows and macOS. Unity 2021.3 or newer.

## Install

In Unity, open **Window ▸ Package Manager**, then **+ ▸ Install package from git URL**, and enter:

```
https://github.com/MarnixWyns/unity-editor-window-icon.git#v1.1.0
```

Or add this line to `Packages/manifest.json`:

```json
"com.biteme.editor-window-icon": "https://github.com/MarnixWyns/unity-editor-window-icon.git#v1.1.0"
```

Then set a **Default Icon** in **Project Settings ▸ Player**. The icon updates within a couple of
seconds, and again whenever you change the Default Icon.

## How it works

- The icon is the Default Icon with the Unity logo of the running editor version as a badge.
- Projects without a Default Icon keep Unity's own icon.

On Windows:

- It becomes the icon of the editor's windows: the title bar and Alt-Tab.
- Windows 11 draws a taskbar button with the icon of its app group, and all Unity editors share a
  single group. The package therefore gives each project its own group (a per-project
  AppUserModelID) with the badged icon, written to `Library/EditorWindowIcon/`. That button
  can sit apart from your other Unity editors.
- If you pin that taskbar button, it reopens this project in the same Unity version.

On macOS:

- It becomes the editor's Dock icon, which the app switcher (Cmd-Tab) shows too. Every open editor
  already has a Dock icon of its own.
- The Default Icon is drawn as a macOS app icon: a rounded square with the standard margin around it.
- While Unity compiles or imports, the Dock shows Unity's own icon with its progress, as usual. The
  badged icon comes back within a couple of seconds after.
- **Options ▸ Keep in Dock** keeps Unity itself in the Dock, not this project.

The package adds nothing to your project's assets or settings, and it's left out of builds.

## License

[CC0 1.0](LICENSE.md): public domain. Use it however you like.
