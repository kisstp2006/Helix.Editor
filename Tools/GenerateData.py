"""Writes what a build would leave in the editor's data folder (Data/) beside its own files: settings.asset, the script
assets the scene names, the scene (a camera clearing to the window's color, the shell's view of UI/Shell.hxml with the
EditorShell component) and assets.index listing every file (not the .meta files). Every image gets its .meta
(readable, so the UI puts it into its atlas). Run it after adding a file (Tools/GenerateSkin.py and
Tools/GenerateIcons.py first):

    python Tools/GenerateData.py
"""

import hashlib
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA = os.path.join(ROOT, "Data")
HEADER = "%YAML 1.1\n%TAG !h! tag:helixengine,2026:\n"
MAIN_SCRIPT_OBJECT = 11400000

# The script classes of the scene: (asset name, module or assembly, namespace, class).
SCRIPTS = [("UIView", "HelixUI", "HelixEngine.UI", "UIView"), ("EditorShell", "HelixEditor.CoreModule", "HelixEditor", "EditorShell")]

IMAGE_META = ("TextureImporter:\n  mipmaps:\n    enableMipMap: 0\n  sRGBTexture: 0\n  isReadable: 1\n"
              "  textureSettings:\n    filterMode: 1\n    wrapU: 1\n    wrapV: 1\n")


def guid(path):
    return hashlib.md5(f"helix-editor:{path}".encode()).hexdigest()


def write(path, text):
    full = os.path.join(DATA, path)
    os.makedirs(os.path.dirname(full), exist_ok=True)
    with open(full, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def scene():
    out = [HEADER]
    out.append("--- !h!1 &100\nGameObject:\n  m_Component:\n  - component: {fileID: 101}\n  - component: {fileID: 102}\n"
               "  m_Name: Editor Camera\n  m_TagString: MainCamera\n  m_IsActive: 1\n")
    out.append("--- !h!4 &101\nTransform:\n  m_GameObject: {fileID: 100}\n  m_Children: []\n  m_Father: {fileID: 0}\n")
    out.append("--- !h!20 &102\nCamera:\n  m_GameObject: {fileID: 100}\n  m_Enabled: 1\n  m_ClearFlags: 2\n"
               "  m_BackGroundColor: {r: 0.137, g: 0.137, b: 0.137, a: 1}\n"
               # After the cameras of the edited scenes (their views come with the scene and game views).
               "  m_Depth: 100\n")
    out.append("--- !h!1 &110\nGameObject:\n  m_Component:\n  - component: {fileID: 111}\n  - component: {fileID: 112}\n"
               "  - component: {fileID: 113}\n  m_Name: Shell\n  m_IsActive: 1\n")
    out.append("--- !h!4 &111\nTransform:\n  m_GameObject: {fileID: 110}\n  m_Children: []\n  m_Father: {fileID: 0}\n")
    out.append(f"--- !h!114 &112\nGameBehaviour:\n  m_GameObject: {{fileID: 110}}\n  m_Enabled: 1\n"
               f"  m_Script: {{fileID: 11500000, guid: {guid('Scripts/UIView.asset')}, type: 3}}\n"
               f"  m_Source: {{fileID: {MAIN_SCRIPT_OBJECT}, guid: {guid('UI/Shell.hxml')}, type: 3}}\n"
               f"  m_SortingOrder: 0\n  m_WatchFiles: 1\n")
    out.append(f"--- !h!114 &113\nGameBehaviour:\n  m_GameObject: {{fileID: 110}}\n  m_Enabled: 1\n"
               f"  m_Script: {{fileID: 11500000, guid: {guid('Scripts/EditorShell.asset')}, type: 3}}\n")
    return "".join(out)


def main():
    for asset, module, namespace, name in SCRIPTS:
        write(f"Scripts/{asset}.asset", f"{HEADER}--- !h!115 &11500000\nMonoScript:\n  m_Name: {name}\n  m_ExecutionOrder: 0\n"
                                        f"  m_ClassName: {name}\n  m_Namespace: {namespace}\n  m_AssemblyName: {module}\n")
    write("Scenes/Editor.helix", scene())
    for folder, _, names in os.walk(DATA):
        for n in names:
            if n.lower().endswith(".png"):
                with open(os.path.join(folder, n + ".meta"), "w", encoding="utf-8", newline="\n") as f:
                    f.write(IMAGE_META)
    write("settings.asset", f"{HEADER}--- !h!129 &1\nPlayerSettings:\n  companyName: Helix\n  productName: Helix Editor\n  bundleVersion: 0.1\n"
                            f"  applicationIdentifier: org.helix.editor\n  runInBackground: 1\n"
                            f"--- !h!141 &2\nBuildSettings:\n  scenes:\n  - Scenes/Editor.helix\n")
    files = []
    for folder, _, names in os.walk(DATA):
        for n in names:
            path = os.path.relpath(os.path.join(folder, n), DATA).replace(os.sep, "/")
            if path not in ("assets.index", "settings.asset") and not path.endswith(".meta"):
                files.append(path)
    lines = ["# The editor's data folder: every file, its guid and its path (Tools/GenerateData.py)."]
    lines += [f"{guid(p)} {p}" for p in sorted(files)]
    write("assets.index", "\n".join(lines) + "\n")


if __name__ == "__main__":
    main()
