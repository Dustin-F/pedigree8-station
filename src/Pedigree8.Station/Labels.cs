using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Pedigree8.Core;

namespace Pedigree8.Station
{
    public static class Labels
    {
        private const double Mm = 96 / 25.4;
        private const double LabelWidth = 60 * Mm;
        private const double LabelHeight = 30 * Mm;
        private const int Columns = 3;
        private const int Rows = 9;

        // Vector QR image (black on white, 4-module quiet zone) so it prints sharp at any size.
        public static DrawingImage QrImage(string qrToken)
        {
            var matrix = QrMatrix.Create(QrPayload.Encode(qrToken));
            var size = matrix.GetLength(0);
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        if (!matrix[x, y])
                        {
                            continue;
                        }

                        ctx.BeginFigure(new Point(x + 4, y + 4), true, true);
                        ctx.PolyLineTo(new[] { new Point(x + 5, y + 4), new Point(x + 5, y + 5), new Point(x + 4, y + 5) }, false, false);
                    }
                }
            }

            geometry.Freeze();
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, size + 8, size + 8))));
            group.Children.Add(new GeometryDrawing(Brushes.Black, null, geometry));
            group.Freeze();
            var image = new DrawingImage(group);
            image.Freeze();
            return image;
        }

        public static FixedDocument Sheet(IReadOnlyList<EnclosureOverview> enclosures)
        {
            var document = new FixedDocument();
            var pageSize = new Size(210 * Mm, 297 * Mm);
            document.DocumentPaginator.PageSize = pageSize;
            var left = (pageSize.Width - Columns * LabelWidth) / 2;
            var top = (pageSize.Height - Rows * LabelHeight) / 2;

            foreach (var pageItems in enclosures.Select((e, i) => (e, i)).GroupBy(x => x.i / (Columns * Rows)))
            {
                var page = new FixedPage { Width = pageSize.Width, Height = pageSize.Height, Background = Brushes.White };
                foreach (var (enclosure, index) in pageItems)
                {
                    var slot = index % (Columns * Rows);
                    var label = Label(enclosure);
                    FixedPage.SetLeft(label, left + slot % Columns * LabelWidth);
                    FixedPage.SetTop(label, top + slot / Columns * LabelHeight);
                    page.Children.Add(label);
                }

                var content = new PageContent();
                ((System.Windows.Markup.IAddChild)content).AddChild(page);
                document.Pages.Add(content);
            }

            return document;
        }

        public static FrameworkElement Label(EnclosureOverview enclosure)
        {
            var grid = new Grid { Width = LabelWidth, Height = LabelHeight, Background = Brushes.White };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28 * Mm) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());

            var qr = new Image { Source = QrImage(enclosure.QrToken), Margin = new Thickness(1.5 * Mm), Stretch = Stretch.Uniform };
            RenderOptions.SetEdgeMode(qr, EdgeMode.Aliased);
            grid.Children.Add(qr);

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 2 * Mm, 0) };
            text.Children.Add(new TextBlock
            {
                Text = enclosure.Code,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.Black,
                TextWrapping = TextWrapping.Wrap,
            });
            text.Children.Add(new TextBlock
            {
                Text = enclosure.IsEmpty ? "" : enclosure.Occupants,
                FontSize = 11,
                Foreground = Brushes.Black,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
            text.Children.Add(new TextBlock { Text = "Pedigree8", FontSize = 7, Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 0) });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            return new Border
            {
                Child = grid,
                BorderBrush = Brushes.LightGray,
                BorderThickness = new Thickness(0.5),
            };
        }

        public static void Print(IReadOnlyList<EnclosureOverview> enclosures)
        {
            if (enclosures.Count == 0)
            {
                return;
            }

            var dialog = new PrintDialog();
            if (dialog.ShowDialog() == true)
            {
                dialog.PrintDocument(Sheet(enclosures).DocumentPaginator, "Pedigree8 enclosure labels");
            }
        }
    }
}
