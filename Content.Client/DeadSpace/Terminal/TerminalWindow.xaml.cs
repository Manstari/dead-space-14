using Content.Shared.DeadSpace.Terminal;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML;
using System.Runtime.CompilerServices;

namespace Content.Client.DeadSpace.Terminal;

public sealed partial class TerminalWindow : DefaultWindow
{
    public NetEntity Terminal;
    public event Action<string>? CommandEntered;
    private readonly LineEdit _commandInput;
    private readonly RichTextLabel _output;

    public TerminalWindow()
    {
        RobustXamlLoader.Load(this);

        _commandInput = FindControl<LineEdit>("CommandInput");
        _output = FindControl<RichTextLabel>("Output");

        _commandInput.OnTextEntered += OnCommandEntered;
    }

    public void SetTerminal(NetEntity terminal)
    {
        Terminal = terminal;
    }

    public void AddColorfullText(string text, string? color)
    {
        if (color != null)
        {
            _output.Text += $"[color=#{color}]{text}[/color]";
            return;
        }
        _output.Text += $"{text}";
    }

    public void AddOutput(string text)
    {
        if (text == "\x01CLEAR")
        {
            _output.SetMessage(string.Empty);
            return;
        }

        _output.Text += text;
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
