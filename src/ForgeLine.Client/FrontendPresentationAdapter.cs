using ForgeLine.Presentation;
using ForgeLine.UI;

namespace ForgeLine.Client;

internal static class FrontendPresentationAdapter
{
    internal static FrontendSurfaceView Loading(
        FrontendLoadingState state) =>
        FrontendSurfaceView.Loading(
            state.Status,
            state.HasDeterminateProgress,
            state.Progress);

    internal static FrontendSurfaceView MainMenu(
        MainMenuModel menu)
    {
        ArgumentNullException.ThrowIfNull(menu);

        FrontendMenuEntryView[] entries =
            menu.Items
                .Select(
                    item =>
                        new FrontendMenuEntryView(
                            item.Id,
                            item.Label,
                            item.IsEnabled,
                            string.Equals(
                                item.Id,
                                menu.FocusedId,
                                StringComparison.Ordinal)))
                .ToArray();

        return FrontendSurfaceView.MainMenu(entries);
    }
}
