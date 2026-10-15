using TMPro;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;

namespace FlickCurling.EditorTools
{
    /// <summary>
    /// Adds Unity Localization Localize*Event components from editor builders. Their listeners are always set to
    /// Editor And Runtime (never Runtime Only), so the localized text also shows and previews in the editor.
    /// </summary>
    public static class LocalizeEvents
    {
        /// <summary>A LocalizeStringEvent on the label's object that writes <paramref name="key"/> of <paramref name="table"/> into its text.</summary>
        public static LocalizeStringEvent BindText(TMP_Text label, string table, string key)
        {
            var loc = label.gameObject.AddComponent<LocalizeStringEvent>();
            loc.StringReference = new LocalizedString(table, key);
            var setText = (UnityAction<string>)System.Delegate.CreateDelegate(typeof(UnityAction<string>), label,
                typeof(TMP_Text).GetProperty(nameof(TMP_Text.text)).GetSetMethod());
            AddEditorAndRuntime(loc.OnUpdateString, setText);
            return loc;
        }

        /// <summary>Adds a persistent listener that also runs in edit mode.</summary>
        public static void AddEditorAndRuntime<T>(UnityEvent<T> evt, UnityAction<T> call)
        {
            UnityEventTools.AddPersistentListener(evt, call);
            evt.SetPersistentListenerState(evt.GetPersistentEventCount() - 1, UnityEventCallState.EditorAndRuntime);
        }
    }
}
