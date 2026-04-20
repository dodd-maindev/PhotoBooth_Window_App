using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Photobooth.Desktop.Services;

public sealed class PrintService
{
    public bool PrintSingle(string imagePath)
    {
        if (!File.Exists(imagePath))
        {
            return false;
        }

        var dialog = new PrintConfirmDialog(imagePath, 1, null);
        dialog.Owner = Application.Current.MainWindow;
        var result = dialog.ShowDialog();
        if (result != true)
        {
            return false;
        }

        if (dialog.ShouldPrintNow)
        {
            return DoPrint(imagePath, 1);
        }
        return true;
    }

    public bool PrintFour(IReadOnlyList<string> imagePaths)
    {
        if (imagePaths.Count == 0)
        {
            return false;
        }

        var dialog = new PrintConfirmDialog(imagePaths[0], imagePaths.Count, null);
        dialog.Owner = Application.Current.MainWindow;
        var result = dialog.ShowDialog();
        if (result != true)
        {
            return false;
        }

        if (dialog.ShouldPrintNow)
        {
            return DoPrint(imagePaths[0], imagePaths.Count);
        }
        return true;
    }

    private bool DoPrint(string imagePath, int count)
    {
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true)
        {
            return false;
        }

        var visual = count == 4
            ? CreateFourUpVisual(GetFourImages(imagePath), printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight)
            : CreateSingleVisual(imagePath, printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight);
        printDialog.PrintVisual(visual, $"Photobooth {(count == 4 ? "4-Up" : "Single")} Print");
        return true;
    }

    private IReadOnlyList<string> GetFourImages(string firstImagePath)
    {
        var dir = Path.GetDirectoryName(firstImagePath) ?? string.Empty;
        var ext = Path.GetExtension(firstImagePath);
        var preview = Path.Combine(dir, "preview_4up" + ext);
        return File.Exists(preview) ? new[] { preview, firstImagePath, firstImagePath, firstImagePath } : new[] { firstImagePath, firstImagePath, firstImagePath, firstImagePath };
    }

    private static FrameworkElement CreateSingleVisual(string imagePath, double width, double height)
    {
        var grid = new Grid
        {
            Width = width,
            Height = height,
            Background = Brushes.White,
            Margin = new Thickness(20)
        };

        var image = new Image
        {
            Source = ImageSourceFactory.LoadFromFile(imagePath),
            Stretch = Stretch.UniformToFill
        };

        grid.Children.Add(image);
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }

    private static FrameworkElement CreateFourUpVisual(IReadOnlyList<string> imagePaths, double width, double height)
    {
        var grid = new Grid
        {
            Width = width,
            Height = height,
            Background = Brushes.White,
            Margin = new Thickness(20)
        };

        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        for (var index = 0; index < 4; index++)
        {
            var row = index / 2;
            var column = index % 2;
            var border = new Border
            {
                Margin = new Thickness(8),
                Background = Brushes.Black,
                CornerRadius = new CornerRadius(8),
                Child = new Image
                {
                    Source = index < imagePaths.Count ? ImageSourceFactory.LoadFromFile(imagePaths[index]) : null,
                    Stretch = Stretch.UniformToFill
                }
            };

            Grid.SetRow(border, row);
            Grid.SetColumn(border, column);
            grid.Children.Add(border);
        }

        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();
        return grid;
    }
}