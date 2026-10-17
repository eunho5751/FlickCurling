# FlickCurling: working notes

- Unity Localization: every Localize*Event component (LocalizeStringEvent, LocalizeAssetEvent, ...) must have its
  listeners set to **Editor And Runtime**, never Runtime Only. Add them through
  `Assets/Editor/LocalizeEvents.cs` (`LocalizeEvents.BindText` / `AddEditorAndRuntime`), which does this.
- Shared UI pieces are prefabs, and screens use instances of them rather than building the same thing again:
  `Assets/Prefabs/Popup.prefab` (PopupBuilder) and the controls in `Assets/Prefabs/UI` (UiPrefabBuilder: Button_Primary,
  Button_Secondary, ToggleGroup, Slider, Dropdown, ScrollView, ShopCard). Fill an instance in with `UiPrefabBuilder.SetLabel` / `SetOptions`. Anything new
  that more than one screen can use becomes a prefab too.
- Every button, toggle and dropdown clicks when pressed through `UiClickSound` (made at start-up, it watches presses in
  every scene), so new buttons need nothing extra. Add `NoClickSound` to one that should stay silent.
