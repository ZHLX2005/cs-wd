namespace CsWd;

public enum ResultKind { Directory, RunCommand, Info }

public class ResultItem
{
    public string Icon { get; init; } = "•";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string TagText { get; init; } = "";
    public ResultKind Kind { get; init; }
    public string Path { get; init; } = "";
    public string Cmd { get; init; } = "";

    public static ResultItem Directory(string path, int count) => new()
    {
        Icon = "📁", Kind = ResultKind.Directory, Path = path, TagText = "Enter",
        Title = System.IO.Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : path,
        Subtitle = $"{path}   ({count}x)",
    };

    public static ResultItem RunCommand(string cmd) => new()
    {
        Icon = "⚡", Kind = ResultKind.RunCommand, Cmd = cmd, TagText = "后台",
        Title = cmd,
        Subtitle = "在后台执行，输出写入日志",
    };

    public static ResultItem Error(string msg) => new()
    {
        Icon = "⚠", Kind = ResultKind.Info, Title = msg, Subtitle = "",
    };
}
