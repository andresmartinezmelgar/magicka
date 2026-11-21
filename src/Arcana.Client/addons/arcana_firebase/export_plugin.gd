@tool
extends EditorPlugin

var exporter: EditorExportPlugin

func _enter_tree() -> void:
    exporter = ArcanaFirebaseExport.new()
    add_export_plugin(exporter)

func _exit_tree() -> void:
    remove_export_plugin(exporter)
    exporter = null

class ArcanaFirebaseExport extends EditorExportPlugin:
    func _get_name() -> String:
        return "ArcanaFirebase"

    func _supports_platform(platform: EditorExportPlatform) -> bool:
        return platform is EditorExportPlatformAndroid

    func _get_android_libraries(_platform: EditorExportPlatform, _debug: bool) -> PackedStringArray:
        return PackedStringArray(["arcana_firebase/ArcanaFirebase.aar"])

    func _get_android_dependencies(_platform: EditorExportPlatform, _debug: bool) -> PackedStringArray:
        return PackedStringArray([
            "com.android.billingclient:billing:8.3.0",
            "com.google.firebase:firebase-auth:25.0.0",
            "androidx.credentials:credentials:1.5.0",
            "androidx.credentials:credentials-play-services-auth:1.5.0",
            "com.google.android.libraries.identity.googleid:googleid:1.2.1",
            "com.google.android.gms:play-services-ads:25.5.0",
            "com.google.android.ump:user-messaging-platform:4.0.0"
        ])
