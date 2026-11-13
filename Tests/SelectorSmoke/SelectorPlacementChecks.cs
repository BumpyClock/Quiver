using Quiver.App.Controls;
using Quiver.App.Helpers;
using Quiver.App.Windows;
using Quiver.Library.Models;
using Windows.Graphics;

namespace Quiver.SelectorSmoke;

internal static class SelectorPlacementChecks
{
    public static void Run()
    {
        var pair = SelectorWindow.GeometryFor(SelectorLayout.Radial, RadialMetrics.For(2));
        var ten = SelectorWindow.GeometryFor(SelectorLayout.Radial, RadialMetrics.For(10));
        var list = SelectorWindow.GeometryFor(SelectorLayout.List, RadialMetrics.For(30));
        foreach (double scale in new[] { 1, 101 / 96.0, 1.25, 1.5, 1.75, 2, 2.5, 3 })
        {
            int width = CursorPosition.NativeLength(ten.Width, scale) + 120;
            int height = CursorPosition.NativeLength(ten.Height, scale) + 80;
            foreach (var origin in new[] { new PointInt32(0, 0), new PointInt32(-3840, -2160), new PointInt32(2560, -1440) })
            {
                var area = new RectInt32(origin.X, origin.Y, width, height);
                foreach (var cursor in new[]
                {
                    new PointInt32(area.X, area.Y), new PointInt32(area.X + width - 1, area.Y),
                    new PointInt32(area.X, area.Y + height - 1), new PointInt32(area.X + width - 1, area.Y + height - 1),
                    new PointInt32(area.X + width / 2, area.Y + height / 2),
                })
                {
                    var opening = new CursorSnapshot(cursor, area, scale);
                    Check(SelectorWindow.EffectiveLayout(SelectorLayout.Radial, RadialMetrics.For(30), opening) == SelectorLayout.List
                        && opening.Fits(ten), "controlled area forces thirty-browser List and fits ten-browser Radial");
                    var before = opening.Place(list);
                    var recovered = opening.Resize(list, pair, new PointInt32(before.X, before.Y));
                    Equal(recovered, opening.Place(pair), "List-to-Radial recovery follows the opening placement policy");
                    Inside(recovered, area, "recovered flower and footer stay in the work area");
                    var initial = opening.Place(pair);
                    var grown = opening.Resize(pair, ten, new PointInt32(initial.X, initial.Y));
                    Inside(grown, area, "two-to-ten Radial growth keeps every target and footer visible");
                    Check(grown.Width == CursorPosition.NativeLength(ten.Width, scale)
                        && grown.Height == CursorPosition.NativeLength(ten.Height, scale), "growth preserves complete radial geometry");
                    int x = initial.X + CursorPosition.NativeLength(pair.Anchor.X, scale) - CursorPosition.NativeLength(ten.Anchor.X, scale);
                    int y = initial.Y + CursorPosition.NativeLength(pair.Anchor.Y, scale) - CursorPosition.NativeLength(ten.Anchor.Y, scale);
                    Check(grown.X == Math.Clamp(x, area.X, area.X + width - grown.Width)
                        && grown.Y == Math.Clamp(y, area.Y, area.Y + height - grown.Height),
                        "same-Radial growth preserves its native anchor with only the displacement needed to fit");
                    Equal(opening.Resize(ten, list, new PointInt32(grown.X, grown.Y)), before,
                        "Radial-to-List transition follows the opening placement policy");
                }
            }
            var unconstrained = new CursorSnapshot(new PointInt32(-100, 200), null, scale);
            var placed = unconstrained.Place(pair);
            var resized = unconstrained.Resize(pair, ten, new PointInt32(placed.X, placed.Y));
            Check(resized.X + CursorPosition.NativeLength(ten.Anchor.X, scale) == unconstrained.Position.X
                && resized.Y + CursorPosition.NativeLength(ten.Anchor.Y, scale) == unconstrained.Position.Y,
                "unconstrained rendering refresh retains the supplied native anchor and scale");
        }
        Console.WriteLine("PASS: List/Radial transitions and two-to-ten Radial growth at four corners, three origins, and eight scales.");
    }

    private static void Inside(RectInt32 bounds, RectInt32 area, string name)
        => Check(bounds.X >= area.X && bounds.Y >= area.Y
            && bounds.X + bounds.Width <= area.X + area.Width && bounds.Y + bounds.Height <= area.Y + area.Height, name);

    private static void Equal(RectInt32 actual, RectInt32 expected, string name)
        => Check(actual.X == expected.X && actual.Y == expected.Y && actual.Width == expected.Width && actual.Height == expected.Height, name);

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception($"Selector placement check failed: {name}");
    }
}
