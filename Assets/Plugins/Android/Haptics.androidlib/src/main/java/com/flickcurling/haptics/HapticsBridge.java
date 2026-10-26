package com.flickcurling.haptics;

import android.content.Context;
import android.os.Build;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.os.VibratorManager;

/**
 * One short vibration of a given length and strength (1..255; a device without strength control vibrates at its own),
 * for the game's hits (Haptics in C#). Never throws: a device without a vibrator just stays still.
 */
public final class HapticsBridge {
    private static Vibrator vibrator;

    private HapticsBridge() { }

    public static void pulse(Context context, int ms, int amplitude) {
        try {
            if (vibrator == null) {
                if (Build.VERSION.SDK_INT >= 31) {
                    VibratorManager manager = (VibratorManager) context.getSystemService(Context.VIBRATOR_MANAGER_SERVICE);
                    vibrator = manager != null ? manager.getDefaultVibrator() : null;
                } else {
                    vibrator = (Vibrator) context.getSystemService(Context.VIBRATOR_SERVICE);
                }
            }
            if (vibrator == null || !vibrator.hasVibrator()) return;
            if (Build.VERSION.SDK_INT >= 26) {
                int a = vibrator.hasAmplitudeControl() ? Math.max(1, Math.min(255, amplitude)) : VibrationEffect.DEFAULT_AMPLITUDE;
                vibrator.vibrate(VibrationEffect.createOneShot(ms, a));
            } else {
                vibrator.vibrate(ms);
            }
        } catch (Exception ignored) {
        }
    }
}
