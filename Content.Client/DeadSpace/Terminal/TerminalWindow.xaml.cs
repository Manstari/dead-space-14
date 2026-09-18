using Content.Shared.DeadSpace.Terminal;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;

namespace Content.Client.DeadSpace.Terminal;

public sealed partial class TerminalWindow : DefaultWindow
{
    public event Action<string>? CommandEntered;
    private readonly LineEdit _commandInput;
    private readonly RichTextLabel _output;
    private readonly ScrollContainer _outputScroll;
    public TerminalWindow()
    {
        RobustXamlLoader.Load(this);

        _commandInput = FindControl<LineEdit>("CommandInput");
        _output = FindControl<RichTextLabel>("Output");
        _outputScroll = FindControl<ScrollContainer>("OutputScroll");

        _commandInput.OnTextEntered += OnCommandEntered;
    }

    private void ScrollToBottom()
    {
        UserInterfaceManager.DeferAction(() =>
        {
            var maxScroll = Math.Max(0f, _output.PixelHeight - _outputScroll.PixelHeight);
            _outputScroll.VScrollTarget = maxScroll;
        });
    }

    public void AddCommand(string prompt, string command)
    {
        _output.Text += $"[color=#16C60C]{prompt}{command}[/color]\n";
        ScrollToBottom();
    }

    public void AddOutput(string text)
    {
        if (text == "\x01CLEAR")
        {
            _output.SetMessage(string.Empty);
            ScrollToBottom();
            return;
        }

        _output.Text += text;

        if (!text.EndsWith('\n'))
            _output.Text += "\n";
        ScrollToBottom();
    }

    private void OnCommandEntered(LineEdit.LineEditEventArgs args)
    {
        CommandEntered?.Invoke(args.Text);

        _commandInput.Clear();
        _commandInput.GrabKeyboardFocus();
    }

    public void FocusInput()
    {
        _commandInput.GrabKeyboardFocus();
    }
}
