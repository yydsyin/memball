// UI text in Chinese, English and French.
// Texts drawn inside the ring must stay short: the ring is only about 38 logical px wide.
using System.Collections.Generic;
using System.Globalization;

static class Lang
{
    public static string Code = "en"; // zh | en | fr

    // "auto" follows the Windows display language: Chinese and French get their own text, everything else English.
    public static string Resolve(string setting)
    {
        if (setting == "zh" || setting == "en" || setting == "fr") return setting;
        string sys = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return sys == "zh" ? "zh" : sys == "fr" ? "fr" : "en";
    }

    public static string T(string key)
    {
        string[] v;
        if (!Texts.TryGetValue(key, out v)) return key;
        return v[Code == "zh" ? 0 : Code == "fr" ? 2 : 1];
    }

    public static string T(string key, params object[] args) { return string.Format(T(key), args); }

    //                                         简体中文                         English                                   Français
    static readonly Dictionary<string, string[]> Texts = new Dictionary<string, string[]>
    {
        // inside the ring
        { "ball.hint",       new[] { "点击清理",                     "Clean",                                   "Nettoyer" } },
        { "ball.cleaning",   new[] { "清理中",                       "Cleaning",                                "Nettoyage" } },
        { "ball.freed",      new[] { "已释放",                       "Freed",                                   "Libéré" } },
        { "ball.optimal1",   new[] { "已是",                         "Memory",                                  "Mémoire" } },
        { "ball.optimal2",   new[] { "最佳",                         "OK",                                      "OK" } },
        { "ball.failed1",    new[] { "清理",                         "Clean",                                   "Nettoyage" } },
        { "ball.failed2",    new[] { "失败",                         "Error",                                   "Échec" } },

        // context menu
        { "menu.memory",     new[] { "内存占用 {0}%",                "Memory {0}%",                             "Mémoire {0} %" } },
        { "menu.top",        new[] { "占用最多的程序",               "Top programs by memory",                  "Programmes les plus gourmands" } },
        { "menu.taskmgr",    new[] { "打开任务管理器",               "Open Task Manager",                       "Ouvrir le Gestionnaire des tâches" } },
        { "menu.clean",      new[] { "清理内存",                     "Clean memory",                            "Nettoyer la mémoire" } },
        { "menu.hide",       new[] { "暂时隐藏",                     "Hide",                                    "Masquer" } },
        { "menu.show",       new[] { "显示悬浮球",                   "Show MemBall",                            "Afficher MemBall" } },
        { "menu.settings",   new[] { "设置…",                        "Settings…",                               "Paramètres…" } },
        { "menu.exit",       new[] { "退出",                         "Exit",                                    "Quitter" } },

        // ending a program from the menu
        { "proc.protected",  new[] { "系统进程，不能结束",           "System process, cannot be ended",         "Processus système, ne peut pas être arrêté" } },
        { "proc.killAll",    new[] { "点击结束全部 {0} 个进程",      "Click to end all {0} processes",          "Cliquer pour arrêter les {0} processus" } },
        { "proc.killOne",    new[] { "点击结束进程",                 "Click to end this program",               "Cliquer pour arrêter ce programme" } },
        { "kill.title",      new[] { "结束进程",                     "End program",                             "Arrêter le programme" } },
        { "kill.confirm",    new[] { "确定要结束“{0}”吗？\n\n{1}占用 {2}。\n该程序中未保存的内容会丢失。",
                                     "End \"{0}\"?\n\n{1}Using {2}.\nUnsaved work in it will be lost.",
                                     "Arrêter « {0} » ?\n\n{1}Utilise {2}.\nLes modifications non enregistrées seront perdues." } },
        { "kill.count",      new[] { "共 {0} 个进程，",              "{0} processes, ",                         "{0} processus, " } },
        { "kill.failed",     new[] { "有 {0} 个进程没能结束（可能受系统保护或权限不足）。",
                                     "{0} process(es) could not be ended (protected by the system or access denied).",
                                     "{0} processus n'ont pas pu être arrêtés (protégés par le système ou accès refusé)." } },

        // tray
        { "tray.tip",        new[] { "MemBall · 内存 {0}%",          "MemBall · Memory {0}%",                   "MemBall · Mémoire {0} %" } },
        { "tray.hidden",     new[] { "MemBall 已隐藏",               "MemBall is hidden",                       "MemBall est masqué" } },
        { "tray.howHotkey",  new[] { "双击托盘图标或按 {0} 恢复显示", "Double-click the tray icon or press {0} to show it again",
                                     "Double-cliquez sur l'icône de la zone de notification ou appuyez sur {0} pour le réafficher" } },
        { "tray.how",        new[] { "双击托盘图标恢复显示",         "Double-click the tray icon to show it again",
                                     "Double-cliquez sur l'icône de la zone de notification pour le réafficher" } },

        // first run
        { "welcome.title",   new[] { "MemBall 已在运行",             "MemBall is running",                      "MemBall est lancé" } },
        { "welcome.text",    new[] { "单击悬浮球清理内存，拖动可移动，右键打开菜单和设置。",
                                     "Click the ball to clean memory, drag it to move it, right-click for the menu and settings.",
                                     "Cliquez sur la boule pour nettoyer la mémoire, faites-la glisser pour la déplacer, clic droit pour le menu et les paramètres." } },

        // autostart and log export
        { "auto.failed",     new[] { "设置开机自启失败。\n\n{0}",     "Could not change starting with Windows.\n\n{0}", "Impossible de modifier le lancement au démarrage.\n\n{0}" } },
        { "auto.runAsAdmin", new[] { "请以管理员身份运行后再试。",   "Run MemBall as administrator and try again.", "Exécutez MemBall en tant qu'administrateur puis réessayez." } },
        { "export.title",    new[] { "导出错误日志",                 "Export error log",                        "Exporter le journal d'erreurs" } },
        { "export.filter",   new[] { "文本文件 (*.txt)|*.txt",       "Text files (*.txt)|*.txt",                "Fichiers texte (*.txt)|*.txt" } },
        { "export.failed",   new[] { "导出失败：{0}",                "Export failed: {0}",                      "Échec de l'exportation : {0}" } },

        // settings window
        { "set.title",       new[] { "MemBall 设置",                 "MemBall Settings",                        "Paramètres de MemBall" } },
        { "set.lang",        new[] { "语言",                         "Language",                                "Langue" } },
        { "set.langAuto",    new[] { "跟随系统",                     "System default",                          "Langue du système" } },
        { "set.autostart",   new[] { "开机自动启动",                 "Start with Windows",                      "Lancer au démarrage de Windows" } },
        { "set.fullscreen",  new[] { "全屏程序运行时自动隐藏",       "Hide while a full-screen app is running", "Masquer pendant une application en plein écran" } },
        { "set.tuck",        new[] { "贴边时自动缩进",               "Tuck into the screen edge when docked",   "Rentrer dans le bord de l'écran une fois ancré" } },
        { "set.tuckDelay",   new[] { "鼠标离开后等待",               "Wait after the mouse leaves",             "Attendre après le départ de la souris" } },
        { "set.seconds",     new[] { "秒",                           "s",                                       "s" } },
        { "set.autoClean",   new[] { "定时清理内存",                 "Clean memory on a schedule",              "Nettoyer la mémoire régulièrement" } },
        { "set.every",       new[] { "每隔",                         "Every",                                   "Toutes les" } },
        { "set.minutes",     new[] { "分钟",                         "min",                                     "min" } },
        { "set.opacity",     new[] { "不透明度",                     "Opacity",                                 "Opacité" } },
        { "set.hotkey",      new[] { "显示/隐藏快捷键",              "Show/hide shortcut",                      "Raccourci afficher/masquer" } },
        { "set.hotkeyNone",  new[] { "不可用（被其他程序占用）",     "Unavailable (used by another program)",   "Indisponible (utilisé par un autre programme)" } },
        { "set.export",      new[] { "导出错误日志…",                "Export error log…",                       "Exporter le journal d'erreurs…" } },
        { "set.version",     new[] { "版本 {0}",                     "Version {0}",                             "Version {0}" } },
        { "set.ok",          new[] { "确定",                         "OK",                                      "OK" } },
        { "set.apply",       new[] { "应用",                         "Apply",                                   "Appliquer" } },
        { "set.cancel",      new[] { "取消",                         "Cancel",                                  "Annuler" } },
        { "crash.gaveUp",    new[] { "MemBall 反复出错，已停止运行。可以从开始菜单重新打开它。错误日志：{0}",
                                     "MemBall kept crashing and has stopped. You can start it again from the Start menu. Error log: {0}",
                                     "MemBall a planté à plusieurs reprises et s'est arrêté. Vous pouvez le relancer depuis le menu Démarrer. Journal d'erreurs : {0}" } },
        { "setup.runFailed", new[] { "MemBall 已安装，但未能启动：{0}\n可以从开始菜单打开它。",
                                     "MemBall was installed but could not be started: {0}\nYou can start it from the Start menu.",
                                     "MemBall est installé mais n'a pas pu démarrer : {0}\nVous pouvez le lancer depuis le menu Démarrer." } },
        { "set.saveFailed",  new[] { "设置无法保存，MemBall 重启后会恢复为原来的设置。详情见错误日志。",
                                     "The settings could not be saved, so they will be lost when MemBall restarts. See the error log for details.",
                                     "Les paramètres n'ont pas pu être enregistrés : ils seront perdus au redémarrage de MemBall. Voir le journal d'erreurs pour les détails." } },

        // updates (Updater.cs)
        { "menu.update",     new[] { "更新到 {0}…",                  "Update to {0}…",                          "Mettre à jour vers {0}…" } },
        { "set.autoUpdate",  new[] { "自动检查更新",                 "Check for updates automatically",         "Rechercher les mises à jour automatiquement" } },
        { "set.checkNow",    new[] { "检查更新",                     "Check now",                               "Vérifier maintenant" } },
        { "upd.checking",    new[] { "正在检查…",                    "Checking…",                               "Vérification…" } },
        { "upd.latest",      new[] { "已是最新版本（{0}）。",         "You have the latest version ({0}).",      "Vous avez la dernière version ({0})." } },
        { "upd.checkFailed", new[] { "检查更新失败：{0}",            "Could not check for updates: {0}",        "Impossible de vérifier les mises à jour : {0}" } },
        { "upd.balloonTitle", new[] { "MemBall 有新版本",            "MemBall update available",                "Mise à jour de MemBall disponible" } },
        { "upd.balloonText", new[] { "{0} 已发布，点击查看。",         "Version {0} is out. Click to see what's new.", "La version {0} est disponible. Cliquez pour voir les nouveautés." } },
        { "upd.window",      new[] { "MemBall 更新",                 "MemBall Update",                          "Mise à jour de MemBall" } },
        { "upd.header",      new[] { "MemBall {0} 已发布",           "MemBall {0} is available",                "MemBall {0} est disponible" } },
        { "upd.current",     new[] { "当前版本：{0}",                "You have version {0}.",                   "Version actuelle : {0}" } },
        { "upd.now",         new[] { "立即更新",                     "Update now",                              "Mettre à jour" } },
        { "upd.later",       new[] { "稍后",                         "Later",                                   "Plus tard" } },
        { "upd.skip",        new[] { "跳过此版本",                   "Skip this version",                       "Ignorer cette version" } },
        { "upd.downloading", new[] { "正在下载… {0}%",               "Downloading… {0}%",                       "Téléchargement… {0} %" } },
        { "upd.installing",  new[] { "正在安装，MemBall 将自动重新启动…", "Installing; MemBall will restart by itself…", "Installation ; MemBall va redémarrer tout seul…" } },
        { "upd.failed",      new[] { "更新失败：{0}",                "Update failed: {0}",                      "Échec de la mise à jour : {0}" } },
        { "upd.badFile",     new[] { "下载的文件校验不通过，已删除。", "The downloaded file failed verification and was deleted.", "Le fichier téléchargé n'a pas passé la vérification et a été supprimé." } },
        { "upd.noAsset",     new[] { "这个版本没有可用的下载文件。",   "This release has no file to download.",   "Cette version n'a aucun fichier à télécharger." } },
        { "upd.openPage",    new[] { "打开下载页面",                 "Open the download page",                  "Ouvrir la page de téléchargement" } },

        // installer (setup\Setup.cs)
        { "setup.title",     new[] { "MemBall {0} 安装",             "MemBall {0} Setup",                       "Installation de MemBall {0}" } },
        { "setup.header",    new[] { "安装 MemBall {0}",             "Install MemBall {0}",                     "Installer MemBall {0}" } },
        { "setup.update",    new[] { "更新 MemBall 到 {0}",          "Update MemBall to {0}",                   "Mettre à jour MemBall vers {0}" } },
        { "setup.desc",      new[] { "内存悬浮球：显示内存占用和网速，一键清理内存。",
                                     "A floating ball that shows memory use and network speed, with one-click memory cleaning.",
                                     "Une boule flottante qui affiche la mémoire utilisée et le débit réseau, avec nettoyage en un clic." } },
        { "setup.location",  new[] { "安装位置",                     "Install location",                        "Emplacement" } },
        { "setup.desktop",   new[] { "创建桌面快捷方式",             "Create a desktop shortcut",               "Créer un raccourci sur le bureau" } },
        { "setup.run",       new[] { "安装完成后运行 MemBall",       "Run MemBall when setup finishes",         "Lancer MemBall à la fin de l'installation" } },
        { "setup.agree",     new[] { "点击“安装”即表示你同意",       "By clicking Install you accept the",      "En cliquant sur Installer, vous acceptez le" } },
        { "setup.license",   new[] { "许可协议",                     "license",                                 "contrat de licence" } },
        { "setup.licenseTitle", new[] { "MemBall 许可协议",          "MemBall License",                         "Contrat de licence de MemBall" } },
        { "setup.install",   new[] { "安装",                         "Install",                                 "Installer" } },
        { "setup.installing", new[] { "正在安装…",                   "Installing…",                             "Installation…" } },
        { "setup.done",      new[] { "MemBall 已安装完成。",          "MemBall has been installed.",             "MemBall a été installé." } },
        { "setup.finish",    new[] { "完成",                         "Finish",                                  "Terminer" } },
        { "setup.failed",    new[] { "安装失败：{0}",                "Setup failed: {0}",                       "Échec de l'installation : {0}" } },
        { "uninst.title",    new[] { "卸载 MemBall",                 "Uninstall MemBall",                       "Désinstaller MemBall" } },
        { "uninst.confirm",  new[] { "确定要从这台电脑上卸载 MemBall 吗？", "Remove MemBall from this computer?", "Supprimer MemBall de cet ordinateur ?" } },
        { "uninst.data",     new[] { "同时删除我的设置和日志",       "Also delete my settings and logs",        "Supprimer aussi mes paramètres et journaux" } },
        { "uninst.button",   new[] { "卸载",                         "Uninstall",                               "Désinstaller" } },
        { "uninst.done",     new[] { "MemBall 已卸载。",              "MemBall has been uninstalled.",           "MemBall a été désinstallé." } },
        { "uninst.failed",   new[] { "卸载失败：{0}",                "Uninstall failed: {0}",                   "Échec de la désinstallation : {0}" } },
    };
}
