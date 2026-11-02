using System.Globalization;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MarsSpringTuner;

public static class MenuTuning
{
    sealed class Panel
    {
        internal VehicleEditor Editor = null!;
        internal GameObject Root = null!;
        internal UISlider[] Sliders = new UISlider[7];
        internal TextMeshPro[] Labels = new TextMeshPro[7];
        internal UIButton Reset = null!, ApplyAll = null!;
        internal Transform Bg = null!, Close = null!;
        internal Vector3 BgScale, ClosePosition;
        internal GameObject? Header;
        internal bool HeaderActive, Active, UndoPushed;
        internal Vehicle.Node? Node;
        internal float[] Values = null!;
    }
    static readonly Dictionary<IntPtr, Panel> panels = new();
    static readonly string[] names = {"Stiffness", "Low speed compression", "High speed compression", "Low speed rebound", "High speed rebound", "Compression crossover", "Rebound crossover"};
    static bool failed;

    public static void Install(Harmony harmony)
    {
        foreach (var pair in new[] { ("HandleInputConfigTool", nameof(Hover)), ("ShowPartSetControls", nameof(Show)), ("HandleSetPartControls", nameof(Update)) })
            harmony.Patch(AccessTools.Method(typeof(VehicleEditor), pair.Item1), postfix: new HarmonyMethod(typeof(MenuTuning), pair.Item2));
    }

    static void Hover(VehicleEditor __instance, Game __0)
    {
        try
        {
            var node = __instance.hoverNodeSelection;
            if (node == null || !NodeSettings.IsSpring(node) || __instance.setControlsNode != null || failed) return;
            // Preserve the game's input action and shortcut remapping; no synthetic bindings on the node.
            Ensure(__instance);
            if (__instance.partConfigText != null)
                __instance.partConfigText.text = "Tune this spring\n" + Control.GetActionString(Control.Action.EditorSetControls) + " Configure suspension";
            if (Control.GetButtonUp(Control.Action.EditorSetControls))
                __instance.ShowPartSetControls(__0, node);
        }
        catch (Exception ex) { Error(ex); }
    }

    static void Show(VehicleEditor __instance, Game __0, Vehicle.Node __1)
    {
        try
        {
            if (panels.TryGetValue(__instance.Pointer, out var old)) Restore(old);
            if (!NodeSettings.IsSpring(__1)) return;
            var p = Ensure(__instance);
            p.Node = __1; p.UndoPushed = false;
            p.Values = NodeSettings.Get(__1) ?? Settings.Defaults()[Damping.Kind(__1.partId)];
            p.HeaderActive = p.Header != null && p.Header.activeSelf;
            p.Header?.SetActive(false);
            p.Bg.localScale = new Vector3(10, p.BgScale.y, p.BgScale.z);
            p.Close.localPosition = new Vector3(3, p.ClosePosition.y, p.ClosePosition.z);
            __instance.autoReturnTickbox.gameObject.SetActive(false);
            __instance.suspensionTickbox.gameObject.SetActive(false);
            __instance.autoReturnTickbox.valueChanged = false;
            __instance.suspensionTickbox.valueChanged = false;
            foreach (var button in __instance.bindingButtons) button.gameObject.SetActive(false);
            p.Active = true;
            Refresh(p);
            p.Root.SetActive(true);
            UIController.FocusTopLeftElem();
        }
        catch (Exception ex) { Error(ex); }
    }

    static Panel Ensure(VehicleEditor editor)
    {
        if (panels.TryGetValue(editor.Pointer, out var existing)) return existing;
        UISlider? template = null;
        foreach (var candidate in Resources.FindObjectsOfTypeAll<UISlider>())
            if (candidate != null && candidate.gameObject.name == "SFXVolume") { template = candidate; break; }
        if (template == null) throw new InvalidOperationException("Native SFXVolume slider template unavailable; suspension menu was not created.");
        var popup = editor.partControlsPopup.transform;
        var p = new Panel { Editor = editor, Bg = popup.Find("BG"), Close = editor.partControlsPopupCloseButton.transform };
        p.BgScale = p.Bg.localScale; p.ClosePosition = p.Close.localPosition;
        var header = popup.Find("BindingsHeader"); p.Header = header == null ? null : header.gameObject;
        p.Root = new GameObject("SpringTuningControls"); p.Root.SetActive(false);
        p.Root.transform.SetParent(popup, false);
        try
        {
            for (int i = 0; i < 7; i++)
            {
                var go = Object.Instantiate(template.gameObject, p.Root.transform);
                go.name = "SpringTuning" + i;
                go.SetActive(true);
                go.transform.localPosition = new Vector3(0, .5f - .5f * i, 0);
                go.transform.localScale = Vector3.one;
                var slider = go.GetComponent<UISlider>();
                var label = go.transform.Find("Label").GetComponent<TextMeshPro>();
                DisableLocalization(go);
                var rt = label.rectTransform;
                rt.localScale = Vector3.one * .0857518f;
                rt.pivot = new Vector2(0, .5f);
                rt.sizeDelta = new Vector2(75, 5);
                rt.anchoredPosition = new Vector2(-4.6f, 0);
                label.fontSize = 24;
                label.enableAutoSizing = false;
                var bg = go.transform.Find("BG");
                bg.localPosition = new Vector3(3.1f, 0, 0);
                bg.localScale = new Vector3(2.4f, .125f, 1);
                slider.Init(); slider.wasChange = false;
                p.Sliders[i] = slider; p.Labels[i] = label;
            }
            var reset = Object.Instantiate(editor.partControlsPopupCloseButton.gameObject, p.Root.transform);
            reset.name = "ResetSpringTuning";
            reset.transform.localPosition = new Vector3(-3, p.ClosePosition.y, 0);
            DisableLocalization(reset);
            foreach (var text in reset.GetComponentsInChildren<TextMeshPro>(true)) text.text = "Reset spring";
            p.Reset = reset.GetComponent<UIButton>(); p.Reset.Init();
            var apply = Object.Instantiate(editor.partControlsPopupCloseButton.gameObject, p.Root.transform);
            apply.name = "ApplyAllSpringTuning";
            apply.transform.localPosition = new Vector3(0, p.ClosePosition.y, 0);
            DisableLocalization(apply);
            foreach (var text in apply.GetComponentsInChildren<TextMeshPro>(true)) text.text = "Apply All";
            p.ApplyAll = apply.GetComponent<UIButton>(); p.ApplyAll.Init();
            panels.Add(editor.Pointer, p);
            return p;
        }
        catch { Object.Destroy(p.Root); throw; }
    }

    static void DisableLocalization(GameObject go)
    {
        foreach (var component in go.GetComponentsInChildren<I2.Loc.Localize>(true)) component.enabled = false;
        foreach (var component in go.GetComponentsInChildren<ControlTextBehaviour>(true)) component.enabled = false;
    }

    static void Update(VehicleEditor __instance)
    {
        try
        {
            if (!panels.TryGetValue(__instance.Pointer, out var p) || !p.Active) return;
            if (__instance.setControlsNode == null || !__instance.partControlsPopup.activeInHierarchy) { Restore(p); return; }
            bool changed = false;
            for (int i = 0; i < 7; i++)
            {
                if (!p.Sliders[i].WasChanged(out float value) || !float.IsFinite(value)) continue;
                float next = SliderScale.Decode(i, Math.Clamp(value, 0, 1));
                if (Math.Abs(next - p.Values[i]) < .000001f) continue;
                PushUndo(p); p.Values[i] = next; changed = true;
            }
            if (p.Reset.Pressed())
            {
                PushUndo(p); NodeSettings.Set(p.Node!, null);
                p.Values = Settings.Defaults()[Damping.Kind(p.Node!.partId)]; Refresh(p); return;
            }
            if (changed) { NodeSettings.Set(p.Node!, (float[])p.Values.Clone()); RefreshLabels(p); }
            if (p.ApplyAll.Pressed()) { ApplyToVehicle(p); global::Audio.PlayUIOpen(); }
        }
        catch (Exception ex) {
            Plugin.Instance?.Log.LogError("Spring tuning change failed: " + ex);
            if(panels.TryGetValue(__instance.Pointer,out var panel)&&panel.Node!=null) {
                panel.Values=NodeSettings.Get(panel.Node)??Settings.Defaults()[Damping.Kind(panel.Node.partId)];Refresh(panel);
            }
        }
    }

    static void PushUndo(Panel p)
    {
        if (p.UndoPushed) return;
        p.Editor.PushUndoStack(); p.UndoPushed = true;
    }
    static IEnumerable<Vehicle.Node> Children(Vehicle.Node node) {
        if(node.children==null)yield break;
        foreach(var child in node.children)if(child!=null)yield return child;
    }
    static void ApplyToVehicle(Panel p) {
        Settings.ValidateValues(p.Values);
        var matches=BulkTuning.Matching(p.Editor.vehicleTree,p.Node!.partId,n=>n.partId,Children,n=>n.Pointer.ToInt64());
        var changed=matches.Where(n=>!(NodeSettings.Get(n)??Settings.Defaults()[Damping.Kind(n.partId)]).SequenceEqual(p.Values)).ToList();
        if(changed.Count==0)return;
        var old=changed.Select(n=>(Node:n,Values:NodeSettings.Get(n))).ToArray();
        // Explicit bulk action is independently undoable, even after editing the source slider.
        p.Editor.PushUndoStack();
        try {foreach(var node in changed)NodeSettings.Set(node,p.Values);}
        catch {foreach(var entry in old)NodeSettings.Set(entry.Node,entry.Values);throw;}
        p.UndoPushed=false;
        Plugin.Instance?.Log.LogInfo($"Apply All copied spring tuning to {changed.Count} matching part(s) on this vehicle.");
    }
    static void Refresh(Panel p)
    {
        for (int i = 0; i < 7; i++) { p.Sliders[i].SetValue(SliderScale.Encode(i, p.Values[i])); p.Sliders[i].wasChange = false; }
        RefreshLabels(p);
    }
    static void RefreshLabels(Panel p)
    {
        bool angular = Damping.Kind(p.Node!.partId) >= 3;
        for (int i = 0; i < 7; i++)
            p.Labels[i].text = names[i] + ": " + p.Values[i].ToString("0.###", CultureInfo.InvariantCulture) + (i >= 5 ? angular ? " rad/s" : " units/s" : "");
    }
    static void Restore(Panel p)
    {
        if (!p.Active) return;
        p.Root.SetActive(false); p.Bg.localScale = p.BgScale; p.Close.localPosition = p.ClosePosition;
        p.Header?.SetActive(p.HeaderActive); p.Active = false; p.Node = null;
        // Original ShowPartSetControls sets binding and checkbox visibility for each subsequent part.
    }
    static void Error(Exception ex)
    {
        if (!failed) Plugin.Instance?.Log.LogError("Suspension Configure controls integration failed: " + ex);
        failed = true;
    }
    public static void Clear()
    {
        foreach (var p in panels.Values) { Restore(p); if (p.Root != null) Object.Destroy(p.Root); }
        panels.Clear(); failed = false;
    }
}
