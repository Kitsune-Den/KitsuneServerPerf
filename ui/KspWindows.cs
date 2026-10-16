using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>
/// Esc menu button that opens the Server Performance window.
/// </summary>
public class XUiC_KspSettingsOpener : XUiController
{
    public override void Init()
    {
        base.Init();
        if (GetChildByType<XUiC_SimpleButton>() is XUiC_SimpleButton button)
        {
            button.OnPressed += (_sender, _mouseButton) => xui.playerUI.windowManager.Open("kspSettings", true);
        }
    }
}

/// <summary>
/// The Server Performance window: two tabs (Fixes, AI throttle), Refresh and Close.
/// Everything shown comes from the server on open; every change goes straight to the
/// server, which applies it, saves Settings.xml and answers with the new state.
/// </summary>
public class XUiC_KspSettingsWindow : XUiController
{
    // Fixes tab: row slots in windows.xml, laid out top to bottom from the server's list.
    const int Slots = 14;
    const int VisibleRows = 12;     // what fits above Refresh / Close
    const int RowTop = 136;
    const int RowStep = 38;
    const int Indent = 24;

    // Groups that get a header toggle switching the whole group (server: "kperf set group:<id>").
    // Other groups get a plain heading.
    static readonly HashSet<string> ToggleGroups = new HashSet<string> { "mods" };

    static readonly (string id, string spec)[] Presets =
    {
        ("gentle", "60:0.05,120:0.025"),
        ("default", "40:0.05,80:0.025"),
        ("strong", "30:0.05,60:0.02"),
    };

    static readonly (string id, string option)[] AiToggles =
    {
        ("tglAiEnemies", "enemies"),
        ("tglAiAnimals", "animals"),
        ("tglAiNpcs", "npcs"),
        ("tglAiAlert", "skipWhenAlert"),
        ("tglAiBloodMoon", "skipDuringBloodMoon"),
    };

    readonly List<(string id, XUiC_SimpleButton button, XUiView page)> tabs = new List<(string, XUiC_SimpleButton, XUiView)>();
    readonly XUiC_ToggleButton[] slotToggles = new XUiC_ToggleButton[Slots];
    readonly XUiV_Label[] slotStates = new XUiV_Label[Slots];
    readonly XUiV_Label[] slotHeadings = new XUiV_Label[Slots];
    readonly string[] slotTargets = new string[Slots];   // fix name or "group:<id>"
    readonly Dictionary<string, XUiC_ToggleButton> aiToggles = new Dictionary<string, XUiC_ToggleButton>();
    readonly List<(string spec, XUiC_SimpleButton button)> presetButtons = new List<(string, XUiC_SimpleButton)>();
    XUiC_ToggleButton aiOn;
    XUiV_Label status;
    XUiV_Label tiersText;
    string currentTab = "fixes";
    int syncedVersion = -1;
    bool syncing;

    public override void Init()
    {
        base.Init();
        status = Label("lblStatus");
        tiersText = Label("lblTiers");

        foreach (string id in new[] { "fixes", "ai" })
        {
            XUiC_SimpleButton button = FindButton("btnTab_" + id);
            XUiView page = GetChildById("kspTab_" + id)?.ViewComponent;
            if (button != null && page != null)
            {
                string tab = id;
                button.OnPressed += (_s, _m) => ShowTab(tab);
                tabs.Add((id, button, page));
            }
        }

        for (int i = 0; i < Slots; i++)
        {
            int slot = i;
            slotToggles[i] = Toggle("tglFix" + i);
            slotStates[i] = Label("lblFixState" + i);
            slotHeadings[i] = Label("lblHeading" + i);
            if (slotToggles[i] != null)
            {
                slotToggles[i].OnValueChanged += (_s, on) =>
                {
                    if (!syncing && slotTargets[slot] != null)
                    {
                        KspClient.SetFix(slotTargets[slot], on);
                    }
                };
            }
        }

        aiOn = Toggle("tglAiOn");
        if (aiOn != null)
        {
            aiOn.OnValueChanged += (_s, on) =>
            {
                if (!syncing)
                {
                    KspClient.SetFix("AiThrottle", on);
                }
            };
        }
        foreach ((string id, string option) in AiToggles)
        {
            XUiC_ToggleButton t = Toggle(id);
            if (t == null)
            {
                continue;
            }
            string opt = option;
            t.OnValueChanged += (_s, on) =>
            {
                if (!syncing)
                {
                    KspClient.SetAiOption(opt, on);
                }
            };
            aiToggles[option] = t;
        }

        foreach ((string id, string spec) in Presets)
        {
            if (FindButton("btnPreset_" + id) is XUiC_SimpleButton b)
            {
                string s = spec;
                b.OnPressed += (_s, _m) => KspClient.SetTiers(s);
                presetButtons.Add((spec, b));
            }
        }

        if (FindButton("btnRefresh") is XUiC_SimpleButton refresh)
        {
            refresh.OnPressed += (_s, _m) => KspClient.Refresh();
        }
        if (FindButton("btnClose") is XUiC_SimpleButton close)
        {
            close.OnPressed += (_s, _m) => xui.playerUI.windowManager.Close("kspSettings");
        }
    }

    public override void OnOpen()
    {
        base.OnOpen();
        ShowTab(currentTab);
        Sync();
        KspClient.Refresh();
    }

    public override void Update(float _dt)
    {
        base.Update(_dt);
        KspClient.Tick();
        if (syncedVersion != KspClient.Version)
        {
            Sync();
        }
    }

    void ShowTab(string tab)
    {
        currentTab = tab;
        foreach ((string id, XUiC_SimpleButton button, XUiView page) in tabs)
        {
            page.IsVisible = id == tab;
            if (button.Button != null)
            {
                button.Button.Selected = id == tab;
            }
        }
    }

    void Sync()
    {
        syncedVersion = KspClient.Version;
        syncing = true;
        try
        {
            if (status != null)
            {
                status.Text = KspClient.Status;
            }

            LayOutFixRows();

            KspClient.FixInfo ai = KspClient.Find("AiThrottle");
            if (aiOn != null)
            {
                aiOn.ViewComponent.IsVisible = ai != null;
                if (ai != null)
                {
                    aiOn.Value = ai.Enabled;
                }
            }
            foreach (KeyValuePair<string, XUiC_ToggleButton> kv in aiToggles)
            {
                kv.Value.ViewComponent.IsVisible = KspClient.HasData;
                kv.Value.Value = KspClient.AiOption(kv.Key);
            }

            string current = string.Join(",", KspClient.Tiers.Select(t => Num(t.distance) + ":" + Num(t.scale)));
            foreach ((string spec, XUiC_SimpleButton button) in presetButtons)
            {
                button.ViewComponent.IsVisible = KspClient.HasData;
                if (button.Button != null)
                {
                    button.Button.Selected = spec == current;
                }
            }
            if (tiersText != null)
            {
                tiersText.Text = !KspClient.HasData ? ""
                    : KspClient.Tiers.Count == 0 ? Localization.Get("kspTiersNone")
                    : string.Format(Localization.Get("kspTiersNow"), string.Join(",  ", KspClient.Tiers.Select(t => $"{Num(t.distance)} m → {Num(t.scale)}")));
            }
        }
        finally
        {
            syncing = false;
        }
    }

    enum RowKind { Heading, GroupToggle, Fix }

    // Groups in the order the server lists them; each group is a heading (or a toggle that
    // switches the whole group) followed by its fixes, indented under a group toggle.
    void LayOutFixRows()
    {
        var rows = new List<(RowKind kind, string group, List<KspClient.FixInfo> members, KspClient.FixInfo fix)>();
        if (KspClient.HasData)
        {
            List<KspClient.FixInfo> fixes = KspClient.Fixes.Where(f => f.Group != "gameplay").ToList();
            foreach (string group in fixes.Select(f => f.Group).Distinct().ToList())
            {
                List<KspClient.FixInfo> members = fixes.Where(f => f.Group == group).ToList();
                rows.Add((ToggleGroups.Contains(group) ? RowKind.GroupToggle : RowKind.Heading, group, members, null));
                foreach (KspClient.FixInfo f in members)
                {
                    rows.Add((RowKind.Fix, group, null, f));
                }
            }
        }

        for (int i = 0; i < Slots; i++)
        {
            XUiC_ToggleButton toggle = slotToggles[i];
            XUiV_Label state = slotStates[i];
            XUiV_Label heading = slotHeadings[i];
            bool used = i < rows.Count && i < VisibleRows;
            int y = -(RowTop + RowStep * i);
            slotTargets[i] = null;

            RowKind kind = used ? rows[i].kind : RowKind.Heading;
            if (heading != null)
            {
                heading.IsVisible = used && kind == RowKind.Heading;
                heading.Position = new Vector2i(22, y - 17);
                heading.Text = used ? GroupName(rows[i].group) : "";
            }
            if (toggle != null)
            {
                toggle.ViewComponent.IsVisible = used && kind != RowKind.Heading;
            }
            if (state != null)
            {
                state.IsVisible = used && kind != RowKind.Heading;
            }
            if (!used || kind == RowKind.Heading || toggle == null)
            {
                continue;
            }

            if (kind == RowKind.GroupToggle)
            {
                List<KspClient.FixInfo> members = rows[i].members;
                int on = members.Count(f => f.Enabled);
                slotTargets[i] = "group:" + rows[i].group;
                toggle.ViewComponent.Position = new Vector2i(20, y);
                toggle.Label = GroupName(rows[i].group);
                toggle.Value = on == members.Count;
                if (state != null)
                {
                    state.Text = string.Format(Localization.Get("kspGroupCount"), on, members.Count);
                }
            }
            else
            {
                KspClient.FixInfo f = rows[i].fix;
                bool indented = ToggleGroups.Contains(rows[i].group);
                slotTargets[i] = f.Name;
                toggle.ViewComponent.Position = new Vector2i(indented ? 20 + Indent : 20, y);
                toggle.Label = f.Label;
                toggle.Value = f.Enabled;
                if (state != null)
                {
                    state.Text = StateText(f);
                }
            }
        }
    }

    static string GroupName(string group)
    {
        string key = "kspGroup_" + group;
        string name = Localization.Get(key);
        return string.IsNullOrEmpty(name) || name == key ? group : name;
    }

    static string StateText(KspClient.FixInfo f)
    {
        switch (f.State)
        {
            case "On": return Localization.Get("kspStateOn");
            case "Off": return Localization.Get("kspStateOff");
            case "Waiting": return Localization.Get("kspStateWaiting");
            case "Skipped": return string.IsNullOrEmpty(f.Reason) ? Localization.Get("kspStateSkipped") : f.Reason;
            default: return Localization.Get("kspStateFailed");
        }
    }

    static string Num(float f) => f.ToString("0.####", CultureInfo.InvariantCulture);

    XUiC_ToggleButton Toggle(string id)
    {
        XUiController c = GetChildById(id);
        return c as XUiC_ToggleButton ?? c?.GetChildByType<XUiC_ToggleButton>();
    }

    XUiC_SimpleButton FindButton(string id)
    {
        XUiController c = GetChildById(id);
        return c as XUiC_SimpleButton ?? c?.GetChildByType<XUiC_SimpleButton>();
    }

    XUiV_Label Label(string id) => GetChildById(id)?.ViewComponent as XUiV_Label;
}
