using UnityEngine;

// Playing cards without any art assets: one texture holding all 52 faces
// plus a card back is drawn in code the first time it's needed (ranks from
// a tiny 5x7 pixel font, suits from simple shape formulas), and each card
// is a two-sided quad whose UVs pick its cell out of that texture. All
// cards share one material.
//
// A card is a number 0-51: rank = card % 13 (0 = Ace ... 12 = King),
// suit = card / 13 (0 spades, 1 hearts, 2 diamonds, 3 clubs).
public static class CardAtlas
{
    public const float CardWidth = 0.085f;
    public const float CardHeight = 0.12f;

    const int Columns = 13, Rows = 5; // 4 suit rows + 1 row holding the back
    const int CellW = 128, CellH = 180;

    static readonly string[] RankNames = { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K" };

    static readonly string Glyphs = "A234567891 0JQK";
    static readonly string[] Font =
    {
        "01110100011000111111100011000110001", // A
        "01110100010000100010001000100011111", // 2
        "11110000010000101110000010000111110", // 3
        "00010001100101010010111110001000010", // 4
        "11111100001111000001000011000101110", // 5
        "00110010001000011110100011000101110", // 6
        "11111000010001000100010000100001000", // 7
        "01110100011000101110100011000101110", // 8
        "01110100011000101111000010001001100", // 9
        "00100011000010000100001000010001110", // 1
        "00000000000000000000000000000000000", // (space)
        "01110100011001110101110011000101110", // 0
        "00111000100001000010000101001001100", // J
        "01110100011000110001101011001001101", // Q
        "10001100101010011000101001001010001", // K
    };

    static Texture2D _texture;
    static Material _material;
    static readonly Mesh[] _meshes = new Mesh[52];

    public static string RankName(int card) => RankNames[card % 13];

    // Blackjack value: face cards 10, ace 11 (the hand logic drops it to 1).
    public static int Value(int card)
    {
        int rank = card % 13;
        return rank == 0 ? 11 : rank >= 9 ? 10 : rank + 1;
    }

    // A card lying flat: face up along +Y, back along -Y, its top edge
    // toward local -Z - so with no rotation it reads the right way up for
    // someone standing on the +Z side.
    public static GameObject CreateCard(int card, Material baseMaterial)
    {
        if (_material == null)
        {
            _material = new Material(baseMaterial != null ? baseMaterial : new Material(Shader.Find("Universal Render Pipeline/Lit")))
            {
                name = "Playing Cards (runtime)",
            };
            _material.SetTexture("_BaseMap", Texture);
            _material.color = Color.white;
        }

        var go = new GameObject($"Card {RankName(card)}");
        go.AddComponent<MeshFilter>().sharedMesh = CardMesh(card);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = _material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return go;
    }

    static Mesh CardMesh(int card)
    {
        if (_meshes[card] != null) return _meshes[card];

        float w = CardWidth * 0.5f, h = CardHeight * 0.5f;
        const float t = 0.0006f;
        Vector2 face = new Vector2(card % 13, card / 13), back = new Vector2(0f, 4f);

        // Seen from the +Z side looking toward -Z, the viewer's right is -X
        // and the card's top is -Z: u runs toward -X, v toward -Z.
        Vector2 Uv(Vector2 cell, float x, float z, bool mirror)
        {
            float u = 0.5f - x / CardWidth;
            if (mirror) u = 1f - u;
            float v = 0.5f - z / CardHeight;
            return new Vector2((cell.x + u) / Columns, (cell.y + v) / Rows);
        }

        var vertices = new Vector3[8];
        var uvs = new Vector2[8];
        float[] xs = { -w, -w, w, w }, zs = { -h, h, h, -h };
        for (int i = 0; i < 4; i++)
        {
            vertices[i] = new Vector3(xs[i], t, zs[i]);
            uvs[i] = Uv(face, xs[i], zs[i], false);
            vertices[4 + i] = new Vector3(xs[i], -t, zs[i]);
            uvs[4 + i] = Uv(back, xs[i], zs[i], true);
        }

        var mesh = new Mesh { name = $"Card {card}" };
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, /* back, wound the other way */ 4, 6, 5, 4, 7, 6 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return _meshes[card] = mesh;
    }

    // ------------------------------------------------------------ drawing

    static Texture2D Texture
    {
        get
        {
            if (_texture != null) return _texture;

            var pixels = new Color32[Columns * CellW * Rows * CellH];
            for (int suit = 0; suit < 4; suit++)
                for (int rank = 0; rank < 13; rank++)
                    DrawFace(pixels, rank, suit);
            DrawBack(pixels, 0, 4);

            _texture = new Texture2D(Columns * CellW, Rows * CellH, TextureFormat.RGBA32, true)
            {
                name = "Playing Cards",
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 4,
            };
            _texture.SetPixels32(pixels);
            _texture.Apply(true, true);
            return _texture;
        }
    }

    // (x, y) inside a cell, origin top-left.
    static void Set(Color32[] pixels, int column, int row, int x, int y, Color32 colour)
    {
        if (x < 0 || y < 0 || x >= CellW || y >= CellH) return;
        int px = column * CellW + x, py = row * CellH + (CellH - 1 - y);
        pixels[py * Columns * CellW + px] = colour;
    }

    static void Blend(Color32[] pixels, int column, int row, int x, int y, Color32 colour, float amount)
    {
        if (amount <= 0f || x < 0 || y < 0 || x >= CellW || y >= CellH) return;
        int px = column * CellW + x, py = row * CellH + (CellH - 1 - y);
        int i = py * Columns * CellW + px;
        pixels[i] = Color32.Lerp(pixels[i], colour, amount);
    }

    static void DrawFace(Color32[] pixels, int rank, int suit)
    {
        var paper = new Color32(250, 248, 240, 255);
        var edge = new Color32(190, 185, 175, 255);
        Color32 ink = suit == 1 || suit == 2 ? new Color32(200, 25, 35, 255) : new Color32(20, 20, 25, 255);

        for (int y = 0; y < CellH; y++)
            for (int x = 0; x < CellW; x++)
                Set(pixels, rank, suit, x, y, x < 3 || y < 3 || x >= CellW - 3 || y >= CellH - 3 ? edge : paper);

        // Corner index: rank over a small suit, top-left...
        string name = RankNames[rank];
        int scale = 5;
        int textWidth = name.Length * 6 * (name.Length > 1 ? 4 : scale);
        DrawText(pixels, rank, suit, name, 10, 10, name.Length > 1 ? 4 : scale, scale, ink);
        DrawSuit(pixels, rank, suit, suit, 10 + Mathf.Min(textWidth, 30) / 2, 60, 11, ink);

        // ...and the same again upside down in the bottom-right corner.
        for (int y = 6; y < 80; y++)
            for (int x = 6; x < 60; x++)
            {
                int px = rank * CellW + x, py = suit * CellH + (CellH - 1 - y);
                Set(pixels, rank, suit, CellW - 1 - x, CellH - 1 - y, pixels[py * Columns * CellW + px]);
            }

        // Middle: one big suit, or a big letter over a suit for J/Q/K.
        if (rank >= 10)
        {
            DrawText(pixels, rank, suit, name, CellW / 2 - 5 * 9 / 2, 50, 9, 9, ink);
            DrawSuit(pixels, rank, suit, suit, CellW / 2, 135, 13, ink);
        }
        else
        {
            DrawSuit(pixels, rank, suit, suit, CellW / 2, CellH / 2, 30, ink);
        }
    }

    static void DrawText(Color32[] pixels, int column, int row, string text, int x, int y, int scaleX, int scaleY, Color32 ink)
    {
        foreach (char c in text)
        {
            int glyph = Glyphs.IndexOf(c);
            if (glyph >= 0)
                for (int gy = 0; gy < 7; gy++)
                    for (int gx = 0; gx < 5; gx++)
                        if (Font[glyph][gy * 5 + gx] == '1')
                            for (int sy = 0; sy < scaleY; sy++)
                                for (int sx = 0; sx < scaleX; sx++)
                                    Set(pixels, column, row, x + gx * scaleX + sx, y + gy * scaleY + sy, ink);
            x += 6 * scaleX;
        }
    }

    // Suit shape centred on (cx, cy) with the given half-size, anti-aliased
    // by sampling each pixel 3x3.
    static void DrawSuit(Color32[] pixels, int column, int row, int suit, int cx, int cy, int size, Color32 ink)
    {
        for (int y = -size - 1; y <= size + 1; y++)
            for (int x = -size - 1; x <= size + 1; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < 3; sy++)
                    for (int sx = 0; sx < 3; sx++)
                        if (InSuit(suit, (x + (sx + 0.5f) / 3f) / size, -(y + (sy + 0.5f) / 3f) / size)) hits++;
                Blend(pixels, column, row, cx + x, cy + y, ink, hits / 9f);
            }
    }

    // p in -1..1, y up.
    static bool InSuit(int suit, float x, float y)
    {
        switch (suit)
        {
            case 2: // diamond
                return Mathf.Abs(x) * 1.3f + Mathf.Abs(y) <= 1f;
            case 1: // heart
                return Heart(x, y);
            case 0: // spade: an upside-down heart on a stem
                return Heart(x, -(y - 0.1f)) || Stem(x, y);
            default: // club: three lobes on a stem
                return Disc(x, y, 0f, 0.42f, 0.36f) || Disc(x, y, -0.42f, -0.12f, 0.36f) || Disc(x, y, 0.42f, -0.12f, 0.36f)
                    || (Mathf.Abs(x) < 0.2f && Mathf.Abs(y) < 0.3f) || Stem(x, y);
        }
    }

    static bool Heart(float x, float y)
    {
        x *= 1.2f;
        y = y * 1.2f + 0.15f;
        float a = x * x + y * y - 1f;
        return a * a * a - x * x * y * y * y <= 0f;
    }

    static bool Stem(float x, float y) => y < -0.2f && y > -1f && Mathf.Abs(x) < 0.07f + (-0.2f - y) * 0.3f;

    static bool Disc(float x, float y, float cx, float cy, float r) => (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;

    static void DrawBack(Color32[] pixels, int column, int row)
    {
        var white = new Color32(250, 248, 240, 255);
        var red = new Color32(170, 25, 40, 255);
        var darkRed = new Color32(120, 15, 28, 255);
        for (int y = 0; y < CellH; y++)
            for (int x = 0; x < CellW; x++)
            {
                bool border = x < 9 || y < 9 || x >= CellW - 9 || y >= CellH - 9;
                bool lattice = ((x + y) / 7 + (x - y + 1000) / 7) % 2 == 0;
                Set(pixels, column, row, x, y, border ? white : lattice ? red : darkRed);
            }
    }
}
