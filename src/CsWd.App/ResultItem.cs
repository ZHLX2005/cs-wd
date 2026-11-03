namespace CsWd;

public enum ResultKind { Directory, RunCommand, Info }

public class ResultItem
{
    public string Icon { get; init; } = "•";
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public ResultKind Kind { get; init; }
    public string Path { get; init; } = "";
    public string Cmd { get; init; } = "";

    public static ResultItem Directory(string path, int count) => new()
    {
        Icon = "📁", Kind = ResultKind.Directory, Path = path,
        Title = System.IO.Path.GetFileName(path.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : path,
        Subtitle = $"{path}   ({count}x)",
    };

    public static ResultItem RunCommand(string cmd) => new()
    {
        Icon = "⚡", Kind = ResultKind.RunCommand, Cmd = cmd,
        Title = $"后台执行  {cmd}",
        Subtitle = "Enter 开始；输出写入 cs-wd 日志，不弹窗",
    };

    public static ResultItem Error(string msg) => new()
    {
        Icon = "⚠", Kind = ResultKind.Info, Title = msg, Subtitle = "",
    };
}
