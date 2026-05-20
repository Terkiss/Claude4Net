using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.DependencyInjection;
using Claude4Net.Cli.Ui;
using Claude4Net.Cli.Ui.Events;
using Claude4Net.Cli.Ui.Approval;
using Claude4Net.Cli.Ui.Rendering;
using Claude4Net.Cli.Ui.Input;
using Claude4Net.SDK;
using Claude4Net.Runtime;

namespace Claude4Net.Tests;

public class K055LumenApprovalFrameTests
{
    private readonly LumenFrameBuilder _builder = new();

    private IServiceProvider CreateMockServiceProvider(out Mock<IInputBroker> brokerMock)
    {
        var services = new ServiceCollection();
        brokerMock = new Mock<IInputBroker>();
        var routerMock = new Mock<ISmartRouter>();
        var approvalMock = new Mock<IRichApprovalHandler>();
        var embeddingMock = new Mock<IEmbeddingProvider>();

        services.AddSingleton(brokerMock.Object);
        services.AddSingleton(routerMock.Object);
        services.AddSingleton(approvalMock.Object);
        services.AddSingleton(embeddingMock.Object);

        services.AddSingleton<ILumenFrameBuilder, LumenFrameBuilder>();
        services.AddSingleton<ILumenTerminalRenderer>(new Mock<ILumenTerminalRenderer>().Object);

        var sp = services.BuildServiceProvider();
        var orchestratorMock = new Mock<ToolOrchestrator>(new List<ITool>(), approvalMock.Object, sp);
        services.AddSingleton(orchestratorMock.Object);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void VisibleApprovalState_GeneratesDialogLines()
    {
        // 1. visible approval state가 frame에 dialog line을 생성한다.
        var state = new LumenState();
        var openedEvent = new ApprovalDialogOpenedEvent(
            "req-1", "Test Title", "Test Description", "Medium", "Preview"
        );
        state = LumenReducer.Reduce(state, openedEvent);

        var metrics = new TerminalMetrics(80, 20, true, false);
        var frame = _builder.Build(state, metrics, "user input", 0);

        Assert.Contains(frame.Lines, l => l.Kind == DisplayLineKind.Dialog);
    }

    [Fact]
    public void DialogLines_ContainRequiredMetadata()
    {
        // 2. dialog line에 tool/risk/title/key hint가 포함된다.
        var state = new LumenState();
        var openedEvent = new ApprovalDialogOpenedEvent(
            "req-1", "Approval Required: my_tool", "Tool Description", "High", "Preview content"
        );
        state = LumenReducer.Reduce(state, openedEvent);

        var metrics = new TerminalMetrics(80, 20, true, false);
        var frame = _builder.Build(state, metrics, "user input", 0);

        var dialogText = string.Join("\n", frame.Lines.Where(l => l.Kind == DisplayLineKind.Dialog).Select(l => l.Text));

        Assert.Contains("Approval Required: my_tool", dialogText);
        Assert.Contains("High", dialogText);
        Assert.Contains("Tool Description", dialogText);
        Assert.Contains("[Y/Enter] Approve", dialogText);
        Assert.Contains("[N] Deny", dialogText);
        Assert.Contains("[Esc] Cancel", dialogText);
    }

    [Fact]
    public void DetailMode_DisplaysPreviewContent()
    {
        // 3. detail mode에서 preview/diff가 표시된다.
        var state = new LumenState();
        var openedEvent = new ApprovalDialogOpenedEvent(
            "req-1", "Title", "Desc", "Medium", "My unique preview content here"
        );
        state = LumenReducer.Reduce(state, openedEvent);
        state = LumenReducer.Reduce(state, new ApprovalDialogDetailToggledEvent()); // Toggle detail mode on

        var metrics = new TerminalMetrics(80, 20, true, false);
        var frame = _builder.Build(state, metrics, "user input", 0);

        var dialogText = string.Join("\n", frame.Lines.Where(l => l.Kind == DisplayLineKind.Dialog).Select(l => l.Text));

        Assert.Contains("My unique preview content here", dialogText);
    }

    [Fact]
    public void CJKPreview_WrapsAndTruncatesSafely()
    {
        // 4. CJK preview가 display-width 기준으로 안전하게 wrap/truncate된다.
        var state = new LumenState();
        // 30 display-width 한글 내용
        var longCjk = "동해물과 백두산이 마르고 닳도록 하느님이 보우하사 우리나라 만세 무궁화 삼천리 화려 강산 대한 사람 대한으로 길이 보전하세";
        var openedEvent = new ApprovalDialogOpenedEvent(
            "req-1", "Title", "Desc", "Medium", longCjk
        );
        state = LumenReducer.Reduce(state, openedEvent);
        state = LumenReducer.Reduce(state, new ApprovalDialogDetailToggledEvent());

        var metrics = new TerminalMetrics(40, 20, true, false);
        var frame = _builder.Build(state, metrics, "input", 0);

        var dialogLines = frame.Lines.Where(l => l.Kind == DisplayLineKind.Dialog).ToList();

        // Check each line fits terminal width (40 columns) and borders are intact
        for (int i = 0; i < dialogLines.Count; i++)
        {
            var line = dialogLines[i];
            Assert.True(TerminalText.DisplayWidth(line.Text) <= 40,
                $"Line '{line.Text}' width ({TerminalText.DisplayWidth(line.Text)}) exceeds 40");

            if (i == 0)
            {
                Assert.StartsWith("┌", line.Text);
                Assert.EndsWith("┐", line.Text);
            }
            else if (i == dialogLines.Count - 1)
            {
                Assert.StartsWith("└", line.Text);
                Assert.EndsWith("┘", line.Text);
            }
            else if (line.Text.StartsWith("├"))
            {
                Assert.EndsWith("┤", line.Text);
            }
            else
            {
                Assert.StartsWith("│ ", line.Text);
                Assert.EndsWith(" │", line.Text);
            }
        }
    }

    [Fact]
    public void DialogVisible_InputAndFooterNotAddedToHistory()
    {
        // 5. dialog visible 상태에서도 input/footer line은 durable history에 추가되지 않는다.
        var state = new LumenState();
        Assert.Empty(state.History);

        var openedEvent = new ApprovalDialogOpenedEvent(
            "req-1", "Title", "Desc", "Medium", "Preview"
        );
        state = LumenReducer.Reduce(state, openedEvent);

        var metrics = new TerminalMetrics(80, 20, true, false);
        var frame = _builder.Build(state, metrics, "current typing buffer", 0);

        // Frame building must not pollute state.History
        Assert.Empty(state.History);

        // Frame must still contain Input and Footer
        Assert.Contains(frame.Lines, l => l.Kind == DisplayLineKind.Input);
        Assert.Contains(frame.Lines, l => l.Kind == DisplayLineKind.Footer);
    }

    [Fact]
    public void DetailModeToggle_ReflectedInFrame()
    {
        // 6. D toggle 이후 frame이 detail 상태를 반영한다.
        var state = new LumenState();
        var openedEvent = new ApprovalDialogOpenedEvent(
            "req-1", "Title", "Desc", "Medium", "Detail Diff content"
        );
        state = LumenReducer.Reduce(state, openedEvent);

        var metrics = new TerminalMetrics(80, 20, true, false);

        // Initially detail mode is OFF
        var frameOff = _builder.Build(state, metrics, "", 0);
        var dialogTextOff = string.Join("\n", frameOff.Lines.Where(l => l.Kind == DisplayLineKind.Dialog).Select(l => l.Text));
        Assert.DoesNotContain("Detail Diff content", dialogTextOff);

        // Toggle details
        state = LumenReducer.Reduce(state, new ApprovalDialogDetailToggledEvent());
        var frameOn = _builder.Build(state, metrics, "", 0);
        var dialogTextOn = string.Join("\n", frameOn.Lines.Where(l => l.Kind == DisplayLineKind.Dialog).Select(l => l.Text));
        Assert.Contains("Detail Diff content", dialogTextOn);
    }

    [Fact]
    public void ResolveActions_RemoveDialogFromFrame()
    {
        // 7. approve/deny/cancel 이후 dialog가 frame에서 사라진다.
        var state = new LumenState();
        var openedEvent = new ApprovalDialogOpenedEvent(
            "req-1", "Title", "Desc", "Medium", "Preview"
        );
        state = LumenReducer.Reduce(state, openedEvent);

        var metrics = new TerminalMetrics(80, 20, true, false);

        // Verify dialog is visible initially
        var frameVisible = _builder.Build(state, metrics, "", 0);
        Assert.Contains(frameVisible.Lines, l => l.Kind == DisplayLineKind.Dialog);

        // Simulate close
        state = LumenReducer.Reduce(state, new ApprovalDialogClosedEvent());
        var frameClosed = _builder.Build(state, metrics, "", 0);
        Assert.DoesNotContain(frameClosed.Lines, l => l.Kind == DisplayLineKind.Dialog);
    }

    [Fact]
    public async Task DialogVisible_NormalKeysDoNotPolluteComposer()
    {
        // 8. dialog visible 중 일반 key 입력이 prompt buffer를 오염시키지 않는다.
        var sp = CreateMockServiceProvider(out _);
        var app = new LumenCliApp(sp);
        var cts = new CancellationTokenSource();

        // 1. Initially buffer is empty
        Assert.Equal("", app._composer.GetState().Text);

        // 2. Open dialog
        app._observer.UpdateState(new ApprovalDialogOpenedEvent("req-1", "Title", "Desc", "Medium", "Preview"));
        Assert.True(app._observer.State.ApprovalDialog.IsVisible);

        // 3. Type standard character 'a' while dialog is active
        await app.ProcessKeyInternalAsync(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false), cts);

        // 4. Buffer must remain empty
        Assert.Equal("", app._composer.GetState().Text);

        // 5. Close dialog (approve)
        await app.ProcessKeyInternalAsync(new ConsoleKeyInfo('y', ConsoleKey.Y, false, false, false), cts);
        Assert.False(app._observer.State.ApprovalDialog.IsVisible);

        // 6. Type 'a' now that dialog is closed
        await app.ProcessKeyInternalAsync(new ConsoleKeyInfo('a', ConsoleKey.A, false, false, false), cts);

        // 7. Buffer must now contain 'a'
        Assert.Equal("a", app._composer.GetState().Text);
    }

    [Fact]
    public void ApprovalDialog_Height3_DoesNotExceedFrameHeight()
    {
        var state = new LumenState();
        var openedEvent = new ApprovalDialogOpenedEvent(
            "req-1", "Test Title", "Test Description", "Medium", "Preview"
        );
        state = LumenReducer.Reduce(state, openedEvent);

        var metrics = new TerminalMetrics(80, 3, true, false);
        var frame = _builder.Build(state, metrics, "user input", 0);

        Assert.Equal(metrics.Height, frame.Lines.Count);
    }

    [Fact]
    public void ApprovalDialog_Height4_DoesNotExceedFrameHeight()
    {
        var state = new LumenState();
        var openedEvent = new ApprovalDialogOpenedEvent(
            "req-1", "Test Title", "Test Description", "Medium", "Preview"
        );
        state = LumenReducer.Reduce(state, openedEvent);

        var metrics = new TerminalMetrics(80, 4, true, false);
        var frame = _builder.Build(state, metrics, "user input", 0);

        Assert.Equal(metrics.Height, frame.Lines.Count);
    }

    [Fact]
    public void ApprovalDialog_NarrowHeight_KeepsInputAndFooter()
    {
        var state = new LumenState();
        var openedEvent = new ApprovalDialogOpenedEvent(
            "req-1", "Test Title", "Test Description", "Medium", "Preview"
        );
        state = LumenReducer.Reduce(state, openedEvent);

        for (int h = 3; h <= 10; h++)
        {
            var metrics = new TerminalMetrics(80, h, true, false);
            var frame = _builder.Build(state, metrics, "user input", 0);

            Assert.Equal(h, frame.Lines.Count);
            Assert.Contains(frame.Lines, l => l.Kind == DisplayLineKind.Input);
            Assert.Contains(frame.Lines, l => l.Kind == DisplayLineKind.Footer);
        }
    }
}
