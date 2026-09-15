using System.Windows;
using System.Windows.Media;
using DnDOverlay.Core;
using DnDOverlay.Rendering.Windows;
using CorePoint = DnDOverlay.Core.Point;
using CoreRect = DnDOverlay.Core.Rect;
using TilePoint = System.Windows.Point;
using TileRect = System.Windows.Rect;

namespace DnDOverlay.Control;

/// <summary>
/// One scene, drawn the way it lies - and nothing else. It knows a <see cref="SceneState"/>, a
/// <see cref="ScreenContext"/> and how the DM is looking at them; it knows nothing about tiles,
/// about the hub, or about which screen this is.
/// <para>
/// <b>That cut is the point.</b> M4 needs the drawing once, M5 three times more: the diagnostic
/// view REPLACES it inside a tile, the scenes tab shows a preview of a SAVED scene, and a layout
/// card shows several of them side by side - "the same rendering as everywhere, only several times
/// and smaller", which Part 7 calls the actual work of that tab. Built into the tile, each of those
/// would be an operation on the open heart.
/// </para>
/// <para>
/// <b>Everything goes through <see cref="Layout.ItemToRect"/></b> (rule 9), and the view rotation is
/// applied to the result rather than to the model: turning the view moves nothing on the table, so
/// nothing about the scene may change here (Part 7).
/// </para>
/// <para>
/// <b>Drawn in one pass, not one element per item.</b> Three pictures moved at the table are about
/// sixty patches a second; a tree of elements re-arranged on each of them is what makes an overview
/// stutter exactly when something is happening (Part 7). What arrives changes the state, and the
/// state is drawn once per render pass - see <see cref="Redraw"/>.
/// </para>
/// </summary>
internal sealed class SceneThumbnail : FrameworkElement
{
    private readonly Pictures _pictures;

    private SceneState _scene = SceneState.Empty;
    private ScreenContext _screen = ScreenContext.Default(new PixelSize(1920, 1080), 96);
    private ViewRotation _view;
    private bool _faded;

    /// <summary>
    /// The card the hand is currently resting on in the fan, drawn clear of the bar and whole. It
    /// is a property of the HAND rather than of the scene, which is why it lives here and not in
    /// the scene state: nobody else's table shows it, and it disappears the moment the hand
    /// finishes (Part 6).
    /// </summary>
    private ItemId? _peek;

    internal SceneThumbnail(Pictures pictures)
    {
        _pictures = pictures;

        // The tile decides how large it is; what is drawn scales into whatever it gets.
        ClipToBounds = true;
    }

    /// <summary>
    /// The shape the drawing wants, and <b>the one place it is worked out</b>: a table turned by a
    /// quarter is upright in the tile, and whoever lays this element out has to give it that shape
    /// or everything drawn in it is stretched (<see cref="Viewing.AspectRatioInView"/>).
    /// <para>
    /// <b>Asked rather than measured.</b> The element does not size itself, because it is laid out
    /// beside two others that must end up on exactly the same rectangle (<see cref="TileFace"/>) -
    /// a second computation of the shape there and here would be two answers to one question
    /// (rule 9).
    /// </para>
    /// </summary>
    internal double AspectRatio => Viewing.AspectRatioInView(_screen.AspectRatio, _view);

    /// <summary>
    /// Whether the pictures stand back so the layer beneath them can be judged - the background
    /// mode (Part 6).
    /// <para>
    /// <b>Transparent rather than veiled.</b> A dark film over the pictures dims the background
    /// along with them, and the background is the one thing being looked at; letting them go
    /// see-through leaves it at its own brightness (hand-run of M4, 38b).
    /// </para>
    /// </summary>
    internal void Faded(bool faded) => _faded = faded;

    /// <summary>Which card is stepped out of the fan for a look, or <see langword="null"/>.</summary>
    internal void Peeking(ItemId? card) => _peek = card;

    /// <summary>
    /// What to draw from now on. It does not draw - the redraw does, once per render pass, so that
    /// twenty arriving patches cost one drawing rather than twenty.
    /// </summary>
    internal void Show(SceneState scene, ScreenContext screen, ViewRotation view)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(screen);

        _scene = scene;
        _screen = screen;
        _view = view;
    }

    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);

        base.OnRender(drawingContext);

        var size = RenderSize;

        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        // The ground is drawn whatever else happens: an empty screen is a screen, and a tile that
        // showed nothing at all would look like a tile that is broken.
        drawingContext.DrawRectangle(
            Brushes.Black, pen: null, new TileRect(0, 0, size.Width, size.Height));

        if (_scene.BackgroundVisible && _scene.Background is { } background)
        {
            Draw(
                drawingContext,
                Layout.BackgroundRect(background, _screen),
                background.RotationDeg,
                _pictures.For(background.AssetId),
                size,
                background.ShowName ? background.Name : null,
                locked: false,
                Parking.Cut.Whole,
                picture0: null);
        }

        if (!_scene.ItemsVisible)
        {
            return;
        }

        if (_faded)
        {
            drawingContext.PushOpacity(0.35);
        }

        // The fan lies above the whole table, and the depth says so. Ordering by it here means the
        // thumbnail and the table cover the same things - one calculation, two surfaces (rule 9).
        // The card being looked at goes on top of everything, which is what "stepped out" means.
        var peeked = _peek is { } looking
            && _scene.Items.Any(item => item.ItemId == looking && item.Parked)
                ? _peek
                : null;

        foreach (var item in _scene.Items
            .OrderBy(item => item.ItemId == peeked ? 1 : 0)
            .ThenBy(item => Parking.Depth(item)))
        {
            // A card under the hand is drawn where it would STEP OUT to, and that place comes from
            // the same arithmetic the table uses - out along the fan at its own slot, not under the
            // finger and not where the hand first landed (Parking.Peek).
            var shown = item.ItemId == peeked && Parking.Peek(_scene, _screen, item.ItemId) is { } clear
                ? item with { CenterX = clear.X, CenterY = clear.Y }
                : item;

            // <b>The tile cuts a fan card exactly as the table does.</b> The window a long card
            // shows of itself is arithmetic in Core, and until now the thumbnail simply did not ask
            // for it - so a card too long to lie in the bar was drawn whole here and trimmed there,
            // which is two different fans (rule 9). A card stepped out for a look is whole, because
            // that is what stepping out means.
            var cut = item.Parked && item.ItemId != peeked
                ? Parking.CutOf(_scene, _screen, item.ItemId)
                : Parking.Cut.Whole;

            Draw(
                drawingContext,
                Layout.ItemToRect(shown, _screen),
                item.RotationDeg,
                item is ImageItem image ? _pictures.For(image.AssetId) : null,
                size,
                item is ImageItem { ShowName: true } named ? named.Name : null,
                item.Locked,
                cut,
                item as ImageItem);
        }

        if (_faded)
        {
            drawingContext.Pop();
        }
    }

    /// <summary>
    /// One picture, from normalised scene coordinates into this element. The rectangle turns with
    /// the view, the angle turns with it too, and the picture is stretched into what comes out -
    /// the anisotropy of normalised coordinates is the tile's shape, not the picture's problem
    /// (<see cref="Viewing"/>).
    /// </summary>
    private void Draw(
        DrawingContext drawingContext,
        CoreRect normalised,
        double angleDeg,
        ImageSource? picture,
        Size size,
        string? name,
        bool locked,
        Parking.Cut cut,
        ImageItem? picture0)
    {
        // The mapped box is where the picture ENDS UP; what it is drawn into is that box before
        // the view's own turn, because the turn below carries the view as well (Placing.BeforeTurn).
        var mapped = Placing.InTile(normalised, _view, size);
        var rect = Placing.BeforeTurn(mapped, _view);

        var centre = new TilePoint(mapped.X + (mapped.Width / 2), mapped.Y + (mapped.Height / 2));

        // The angle turns with the view as well: a picture standing straight on a table seen from
        // the other side is upside down, and drawing it otherwise would make the thumbnail a
        // different table rather than the same one from another side.
        drawingContext.PushTransform(
            new RotateTransform(Viewing.AngleInView(angleDeg, _view), centre.X, centre.Y));

        var trimmed = !cut.IsWhole;

        if (trimmed)
        {
            drawingContext.PushOpacityMask(Trim(normalised, cut, size));
        }

        if (picture is null)
        {
            // No preview to hand: the arrangement is still the truth, so the place is drawn and
            // says "something lies here" rather than nothing at all.
            drawingContext.DrawRectangle(Brushes.DimGray, new Pen(Brushes.Gainsboro, 1), rect);
        }
        else
        {
            drawingContext.DrawImage(picture, rect);
        }

        // Inside the turn, like the table: the caption lies IN the picture and the padlock ON it,
        // so both lie the way the picture lies. Drawn after it, so neither is under it.
        Caption(drawingContext, rect, name);
        Padlock(drawingContext, rect, locked);
        Motion(drawingContext, rect, picture0);

        if (trimmed)
        {
            drawingContext.Pop();
        }

        drawingContext.Pop();
    }

    /// <summary>
    /// The gradient that cuts one fan card, in this tile's own coordinates.
    /// <para>
    /// <b>The axis travels through the view, and that is the whole of the difference to the
    /// table.</b> There the fan's axis is the screen's: a bar down the left or right side runs in
    /// Y, one along the top or bottom runs in X, and the element's box is stated in the same
    /// system. Here the card is drawn into a tile that has been turned by the DM's view, so the
    /// scene's Y may well be the tile's X - and a mask laid along the wrong one would cut the card
    /// across instead of along it. Both ends of the axis are therefore MAPPED rather than a
    /// direction being guessed, which also carries the sign: at 90 degrees the head end of the fan
    /// can end up at the far side of the tile, and a fade put on the wrong end would fade away
    /// exactly the part the fan is showing.
    /// </para>
    /// <para>
    /// <b>180 degrees proves nothing about this</b> (Guide <c>C14</c>): the symmetric turn swaps
    /// both ends together and looks right however the axis was chosen. It wants a quarter turn and
    /// a card too long for the bar.
    /// </para>
    /// <para>
    /// Absolute mapping rather than the table's fractions of a bounding box: a drawing context has
    /// no box of its own, so the two ends are named outright. The STOPS are shared, because where
    /// the fades sit is one decision about the fan (<see cref="FanCut"/>).
    /// </para>
    /// </summary>
    private LinearGradientBrush Trim(CoreRect normalised, Parking.Cut cut, Size size)
    {
        var alongY = _screen.ParkEdge is ParkEdge.Left or ParkEdge.Right;

        var head = alongY
            ? new CorePoint(normalised.X + (normalised.Width / 2), normalised.Y)
            : new CorePoint(normalised.X, normalised.Y + (normalised.Height / 2));

        var tail = alongY
            ? new CorePoint(normalised.X + (normalised.Width / 2), normalised.Bottom)
            : new CorePoint(normalised.Right, normalised.Y + (normalised.Height / 2));

        // Into the same space the picture is drawn in: the mask is pushed inside the turn, so its
        // two ends have to be taken back out of the view's angle exactly as the box was.
        var mapped = Placing.InTile(normalised, _view, size);
        var centre = new TilePoint(mapped.X + (mapped.Width / 2), mapped.Y + (mapped.Height / 2));

        return new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = Placing.BeforeTurn(Placing.InTile(head, _view, size), centre, _view),
            EndPoint = Placing.BeforeTurn(Placing.InTile(tail, _view, size), centre, _view),
            GradientStops = FanCut.Stops(cut),
        };
    }

    /// <summary>
    /// The name plate, through <b>the same cascade the table uses</b> - wrap, shorten, drop
    /// (<see cref="CaptionLayout"/>). That is why this project depends on the rendering library at
    /// all: a second name plate would be the fourth time a size was decided here that already
    /// existed elsewhere (Guide <c>G24</c>).
    /// <para>
    /// <b>The size is the control's, not the screen's</b>, and that is the one parameter that
    /// differs. The screen's setting answers "how far away are the players from THAT screen" - a
    /// projector three metres off, a table at arm's length; the tile is always at the DM's own
    /// arm's length. The consequence is deliberate and worth naming: on a small overview tile the
    /// cascade drops the caption altogether, because a third of a thumb-sized picture has no room
    /// for a line of text. The tile then says a picture lies there, and the single view says what
    /// it is called.
    /// </para>
    /// </summary>
    private static void Caption(DrawingContext drawingContext, TileRect rect, string? name)
    {
        if (name is null || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var caption = CaptionLayout.Fit(name, rect.Width, rect.Height);

        if (!caption.IsVisible)
        {
            return;
        }

        var written = CaptionLayout.Written(caption, rect.Width);
        var top = rect.Bottom - written.Height - 2;

        // On a dark strip, for the reason the table has one: white on a light picture is not a
        // caption, it is a guess.
        drawingContext.DrawRectangle(
            new SolidColorBrush(Color.FromArgb(0xC0, 0, 0, 0)),
            pen: null,
            new TileRect(rect.X, top - 2, rect.Width, written.Height + 4));

        drawingContext.DrawText(written, new TilePoint(rect.X, top));
    }

    /// <summary>
    /// The padlock, which Prüfschritt 21 wants to see on all five locked pictures <b>in the
    /// thumbnail</b> as well as at the table.
    /// <para>
    /// <b>It does not scale with the picture</b>, and that is the difference from the table's. There
    /// it is measured against the picture, because there a picture is hand-sized; here five locked
    /// pictures may be thumbnails, and a mark that shrank with them would be exactly the promise
    /// 21 makes and cannot keep. A sign that is only sometimes readable is not a sign.
    /// </para>
    /// </summary>
    private static void Padlock(DrawingContext drawingContext, TileRect rect, bool locked)
    {
        if (!locked || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        const double Size = 12;

        var right = rect.Right - 2;
        var top = rect.Y + 2;

        drawingContext.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
            pen: null,
            new TileRect(right - Size - 4, top, Size + 4, Size + 4),
            3,
            3);

        var body = new TileRect(right - Size - 2, top + 2 + (Size * 0.42), Size, Size * 0.58);

        drawingContext.DrawRoundedRectangle(Brushes.White, pen: null, body, 1, 1);

        // The shackle as an arc over the body - drawn rather than written as a glyph, so a locked
        // picture never depends on a font being present (the table's reason, kept).
        var shackle = new StreamGeometry();

        using (var arc = shackle.Open())
        {
            var left = new TilePoint(body.X + (Size * 0.22), body.Y);
            var over = new TilePoint(body.Right - (Size * 0.22), body.Y);

            arc.BeginFigure(left, isFilled: false, isClosed: false);
            arc.ArcTo(over, new Size(Size * 0.28, Size * 0.28), 0, false, SweepDirection.Clockwise, true, false);
        }

        shackle.Freeze();

        drawingContext.DrawGeometry(brush: null, new Pen(Brushes.White, 1.6), shackle);
    }

    /// <summary>
    /// Whether this picture MOVES - a small play mark, struck through while it is held.
    /// <para>
    /// <b>The thumbnail does not animate, and that is on purpose</b> (Part 7): a tile is a map of
    /// the table, and twenty running GIFs on it would cost the surface that has to stay readable
    /// under load exactly what the load is. But then nothing on the tile said which pictures are
    /// animated at all, and whether the DM had already stopped one - read at the table, where the
    /// only way to find out was to look at the screen itself (Handlauf M4, dritter Lauf).
    /// </para>
    /// <para>
    /// Top LEFT, opposite the padlock, so the two never have to negotiate a corner. Same size and
    /// same plate as the padlock, and it does not scale with the picture either, for the reason
    /// given there: a sign that is only sometimes readable is not a sign.
    /// </para>
    /// </summary>
    private static void Motion(DrawingContext drawingContext, TileRect rect, ImageItem? picture)
    {
        if (picture is not { Meta.IsAnimated: true } || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        const double Size = 12;

        var left = rect.X + 2;
        var top = rect.Y + 2;
        var plate = new TileRect(left, top, Size + 4, Size + 4);

        drawingContext.DrawRoundedRectangle(
            new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), pen: null, plate, 3, 3);

        // A triangle, because a play mark is the one symbol nobody has to be taught - and drawn
        // rather than written, so it never depends on a font being present.
        var play = new StreamGeometry();

        using (var shape = play.Open())
        {
            var tip = new TilePoint(plate.Right - 4.5, plate.Y + ((Size + 4) / 2));

            shape.BeginFigure(new TilePoint(plate.X + 5, plate.Y + 4), isFilled: true, isClosed: true);
            shape.LineTo(tip, isStroked: true, isSmoothJoin: false);
            shape.LineTo(new TilePoint(plate.X + 5, plate.Bottom - 4), isStroked: true, isSmoothJoin: false);
        }

        play.Freeze();

        drawingContext.DrawGeometry(Brushes.White, pen: null, play);

        if (!picture.AnimationPaused)
        {
            return;
        }

        // Struck through rather than swapped for a pause mark: the question the DM is asking is
        // "does this one move?", and the answer "yes, but it is stopped" is one sign with a line
        // through it rather than two signs he has to tell apart.
        drawingContext.DrawLine(
            new Pen(Brushes.White, 1.6),
            new TilePoint(plate.X + 3, plate.Bottom - 3),
            new TilePoint(plate.Right - 3, plate.Y + 3));
    }
}
