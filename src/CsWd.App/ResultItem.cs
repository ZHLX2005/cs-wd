namespace CsWd;

public enum ResultKind { Directory, RunCommand, Link, Info }

public class ResultItem
{
    public string Icon { get; init; } = "•";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string TagText { get; init; } = "";
    public ResultKind Kind { get; init; }
    public string Path { get; init; } = "";
    public string Cmd { get; init; } = "";
    public string Url { get; init; } = "";

    public static ResultItem Directory(string path, int count) => new()
    {
        Icon = "📁", Kind = ResultKind.Directory, Path = path, TagText = "文件夹",
        Title = System.IO.Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : path,
        Subtitle = count > 0
            ? $"{path}   ({count}x)"
            : $"{path}   · VSCode 最近",
    };

    public static ResultItem RunCommand(string cmd) => new()
    {
        Icon = "⚡", Kind = ResultKind.RunCommand, Cmd = cmd, TagText = "后台",
        Title = cmd,
        Subtitle = "在后台执行，输出写入日志",
    };

    /// <summary>br-tb 数据湖里的链接；回车交给系统默认浏览器。</summary>
    public static ResultItem Link(string title, string url, string subtitle) => new()
    {
        Icon = "🌐", Kind = ResultKind.Link, Url = url, TagText = "链接",
        Title = string.IsNullOrWhiteSpace(title) ? url : title,
        Subtitle = subtitle,
    };

    public static ResultItem Error(string msg) => new()
    {
        Icon = "⚠", Kind = ResultKind.Info, Title = msg, Subtitle = "",
    };
}
