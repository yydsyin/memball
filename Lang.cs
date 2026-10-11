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
        { "set.opacity",     new[] { "不透明度",                     "Opacity",                                 "Opacité" } },
        { "set.hotkey",      new[] { "显示/隐藏快捷键",              "Show/hide shortcut",                      "Raccourci afficher/masquer" } },
        { "set.hotkeyNone",  new[] { "不可用（被其他程序占用）",     "Unavailable (used by another program)",   "Indisponible (utilisé par un autre programme)" } },
        { "set.export",      new[] { "导出错误日志…",                "Export error log…",                       "Exporter le journal d'erreurs…" } },
        { "set.version",     new[] { "版本 {0}",                     "Version {0}",                             "Version {0}" } },
        { "set.ok",          new[] { "确定",                         "OK",                                      "OK" } },
        { "set.cancel",      new[] { "取消",                         "Cancel",                                  "Annuler" } },
    };
}
