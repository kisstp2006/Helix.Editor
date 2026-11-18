namespace HelixEditor
{
    // A menu's entry as the native registry gives it (fields split by the unit separator).
    internal struct MenuEntry
    {
        public string name, path, shortcut;
        public bool submenu, separator, enabled, isChecked;

        public static MenuEntry Parse(string text)
        {
            string[] f = text.Split('\u001f');
            var e = new MenuEntry
            {
                separator = f[0] == "s",
                submenu = f[0] == "m",
                enabled = f.Length > 1 && f[1] == "1",
                isChecked = f.Length > 2 && f[2] == "1",
                name = f.Length > 3 ? f[3] : "",
                path = f.Length > 4 ? f[4] : "",
                shortcut = f.Length > 5 ? f[5] : "",
            };
            return e;
        }
    }

    // A main toolbar element as the native registry gives it.
    internal struct ToolbarEntry
    {
        public string id, strip, icon, text, tooltip;
        public ToolbarGroup group;
        public int order;
        public ToolbarElementKind kind;
        public bool on, enabled;

        public static ToolbarEntry Parse(string text)
        {
            string[] f = text.Split('\u001f');
            int.TryParse(f.Length > 1 ? f[1] : "0", out int group);
            int.TryParse(f.Length > 2 ? f[2] : "0", out int order);
            int.TryParse(f.Length > 3 ? f[3] : "0", out int kind);
            return new ToolbarEntry
            {
                id = f[0],
                group = (ToolbarGroup)group,
                order = order,
                kind = (ToolbarElementKind)kind,
                strip = f.Length > 4 ? f[4] : "",
                icon = f.Length > 5 ? f[5] : "",
                text = f.Length > 6 ? f[6] : "",
                tooltip = f.Length > 7 ? f[7] : "",
                on = f.Length > 8 && f[8] == "1",
                enabled = f.Length > 9 && f[9] == "1",
            };
        }
    }
}
