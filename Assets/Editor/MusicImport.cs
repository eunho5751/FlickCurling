using UnityEditor;
using UnityEngine;

namespace FlickCurling.EditorTools
{
    /// <summary>The background music's import settings (Assets/Resources/Music, made by Tools/Music/compose.py): streamed
    /// from disk as Vorbis rather than unpacked into memory, and loaded in the background.</summary>
    public sealed class MusicImport : AssetPostprocessor
    {
        const string Folder = "Assets/Resources/Music/";

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var a = (AudioImporter)assetImporter;
            a.forceToMono = true;
            a.loadInBackground = true;
            var s = a.defaultSampleSettings;
            s.loadType = AudioClipLoadType.Streaming;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            s.preloadAudioData = false;
            a.defaultSampleSettings = s;
        }
    }
}
