# FlickCurling: working notes

- Unity Localization: every Localize*Event component (LocalizeStringEvent, LocalizeAssetEvent, ...) must have its
  listeners set to **Editor And Runtime**, never Runtime Only. Add them through
  `Assets/Editor/LocalizeEvents.cs` (`LocalizeEvents.BindText` / `AddEditorAndRuntime`), which does this.
