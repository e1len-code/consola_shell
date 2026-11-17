using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ConsolaShell;

/// <summary>
/// Tarjeta con título, valor actual y una gráfica de línea de las últimas N muestras.
/// Se dibuja con una Polyline de pocos puntos: casi sin costo de CPU ni GPU.
/// </summary>
public sealed class Grafica : Border
{
    private const double Ancho = 260, Alto = 60;

    private static readonly Brush ColorTitulo = Congelar(new SolidColorBrush(Color.FromRgb(0x7F, 0x8D, 0xA3)));
    private static readonly Brush ColorAlerta = Congelar(new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80)));
    private static readonly PointCollection SinPuntos = Congelar(new PointCollection());
    private static readonly Brush FondoTarjeta = Congelar(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)));

    private readonly double[] _valores;
    private readonly double _maximo;
    private int _cuenta;
    private readonly TextBlock _valor;
    private readonly Polyline _linea;
    private readonly Polygon _area;

    public Grafica(string titulo, double maximo, Color color, int muestras = 60)
    {
        _valores = new double[muestras];
        _maximo = maximo;

        _linea = new Polyline
        {
            Stroke = Congelar(new SolidColorBrush(color)),
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round
        };
        _area = new Polygon { Fill = Congelar(new SolidColorBrush(Color.FromArgb(0x30, color.R, color.G, color.B))) };

        _valor = new TextBlock { FontSize = 26, Foreground = Brushes.White, Text = "—" };
        DockPanel.SetDock(_valor, Dock.Right);

        var cabecera = new DockPanel { Margin = new Thickness(0, 0, 0, 6), LastChildFill = true };
        cabecera.Children.Add(_valor);
        cabecera.Children.Add(new TextBlock
        {
            Text = titulo,
            FontSize = 16,
            Foreground = ColorTitulo,
            VerticalAlignment = VerticalAlignment.Center
        });

        var lienzo = new Canvas { Width = Ancho, Height = Alto, ClipToBounds = true };
        lienzo.Children.Add(_area);
        lienzo.Children.Add(_linea);

        var contenido = new StackPanel();
        contenido.Children.Add(cabecera);
        contenido.Children.Add(lienzo);

        Child = contenido;
        Background = FondoTarjeta;
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(16, 10, 16, 12);
        Margin = new Thickness(6);
    }

    public void Limpiar()
    {
        _cuenta = 0;
        _valor.Text = "—";
        _valor.Foreground = Brushes.White;
        _linea.Points = SinPuntos;
        _area.Points = SinPuntos;
    }

    /// <summary>Agrega una muestra; null deja el texto en "—" sin mover la gráfica.</summary>
    public void Agregar(double? valor, string texto, bool alerta = false)
    {
        _valor.Text = valor is null ? "—" : texto;
        _valor.Foreground = alerta ? ColorAlerta : Brushes.White;
        if (valor is not double v) return;

        if (_cuenta == _valores.Length) Array.Copy(_valores, 1, _valores, 0, _cuenta - 1);
        else _cuenta++;
        _valores[_cuenta - 1] = v;

        double paso = Ancho / (_valores.Length - 1);
        var puntos = new PointCollection(_cuenta);
        for (int i = 0; i < _cuenta; i++)
            puntos.Add(new Point(i * paso, Alto - Math.Clamp(_valores[i] / _maximo, 0, 1) * (Alto - 2) - 1));
        puntos.Freeze();

        var area = new PointCollection(puntos) { new Point((_cuenta - 1) * paso, Alto), new Point(0, Alto) };
        area.Freeze();

        _linea.Points = puntos;
        _area.Points = area;
    }

    private static T Congelar<T>(T objeto) where T : Freezable
    {
        objeto.Freeze();
        return objeto;
    }
}
