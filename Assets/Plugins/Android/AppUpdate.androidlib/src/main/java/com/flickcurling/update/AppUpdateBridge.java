package com.flickcurling.update;

import android.app.Activity;

import com.google.android.play.core.appupdate.AppUpdateInfo;
import com.google.android.play.core.appupdate.AppUpdateManager;
import com.google.android.play.core.appupdate.AppUpdateManagerFactory;
import com.google.android.play.core.appupdate.AppUpdateOptions;
import com.google.android.play.core.install.model.AppUpdateType;
import com.google.android.play.core.install.model.UpdateAvailability;

/**
 * Asks Google Play whether a newer build of the game can be installed, and runs Play's own full-screen "immediate"
 * update for it (the app restarts on the new build). Called from C# (AppUpdate) with a Listener implemented there.
 * Works only for an app installed from Google Play.
 */
public final class AppUpdateBridge {
    public interface Listener {
        void onResult(int code);
    }

    private AppUpdateBridge() { }

    // a newer build Play will put in now (or one already being put in)
    private static boolean available(AppUpdateInfo info) {
        int a = info.updateAvailability();
        return (a == UpdateAvailability.UPDATE_AVAILABLE || a == UpdateAvailability.DEVELOPER_TRIGGERED_UPDATE_IN_PROGRESS)
            && info.isUpdateTypeAllowed(AppUpdateType.IMMEDIATE);
    }

    /** 1: an update is there to take, 0: none, -1: Play could not tell. */
    public static void check(final Activity activity, final Listener listener) {
        try {
            AppUpdateManagerFactory.create(activity).getAppUpdateInfo()
                .addOnSuccessListener(info -> listener.onResult(available(info) ? 1 : 0))
                .addOnFailureListener(e -> listener.onResult(-1));
        } catch (Exception e) {
            listener.onResult(-1);
        }
    }

    /** Play's update screen: 1 taken (the app restarts with it), 0 turned down or none left to take, -1 it failed. */
    public static void start(final Activity activity, final Listener listener) {
        try {
            final AppUpdateManager manager = AppUpdateManagerFactory.create(activity);
            manager.getAppUpdateInfo()
                .addOnSuccessListener(info -> {
                    if (!available(info)) { listener.onResult(0); return; }
                    manager.startUpdateFlow(info, activity, AppUpdateOptions.defaultOptions(AppUpdateType.IMMEDIATE))
                        .addOnSuccessListener(code -> listener.onResult(code == Activity.RESULT_OK ? 1 : code == Activity.RESULT_CANCELED ? 0 : -1))
                        .addOnFailureListener(e -> listener.onResult(-1));
                })
                .addOnFailureListener(e -> listener.onResult(-1));
        } catch (Exception e) {
            listener.onResult(-1);
        }
    }
}
