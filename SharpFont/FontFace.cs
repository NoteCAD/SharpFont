﻿using SharpFont;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace SharpFont {
    public class FontFace {
        Renderer renderer = new Renderer();
        BaseGlyph[] glyphs;
        MetricsEntry[] hmetrics;
        MetricsEntry[] vmetrics;
        CharacterMap charMap;
        FontWeight weight;
        FontStretch stretch;
        FontStyle style;
        int cellAscent;
        int cellDescent;
        int lineHeight;
        int xHeight;
        int capHeight;
        int underlineSize;
        int underlinePosition;
        int strikeoutSize;
        int strikeoutPosition;
        int unitsPerEm;
        bool isFixedWidth;
        bool integerPpems;

        public bool IsFixedWidth => isFixedWidth;
        public FontWeight Weight => weight;
        public FontStretch Stretch => stretch;
        public FontStyle Style => style;

        internal FontFace (
            int unitsPerEm, int cellAscent, int cellDescent, int lineHeight, int xHeight,
            int capHeight, int underlineSize, int underlinePosition, int strikeoutSize,
            int strikeoutPosition, FontWeight weight, FontStretch stretch, FontStyle style,
            BaseGlyph[] glyphs, MetricsEntry[] hmetrics, MetricsEntry[] vmetrics,
            CharacterMap charMap, bool isFixedWidth, bool integerPpems
        ) {
            this.unitsPerEm = unitsPerEm;
            this.cellAscent = cellAscent;
            this.cellDescent = cellDescent;
            this.lineHeight = lineHeight;
            this.xHeight = xHeight;
            this.capHeight = capHeight;
            this.underlineSize = underlineSize;
            this.underlinePosition = underlinePosition;
            this.strikeoutSize = strikeoutSize;
            this.strikeoutPosition = strikeoutPosition;
            this.weight = weight;
            this.stretch = stretch;
            this.style = style;
            this.hmetrics = hmetrics;
            this.vmetrics = vmetrics;
            this.charMap = charMap;
            this.isFixedWidth = isFixedWidth;
            this.integerPpems = integerPpems;
            this.glyphs = glyphs;
        }

        public static float ComputePixelSize (float pointSize, int dpi) => pointSize * dpi / 72;

        public FaceMetrics GetFaceMetrics (float pixelSize) {
            var scale = ComputeScale(pixelSize);
            return new FaceMetrics(
                cellAscent * scale,
                cellDescent * scale,
                lineHeight * scale,
                xHeight * scale,
                capHeight * scale,
                underlineSize * scale,
                underlinePosition * scale,
                strikeoutSize * scale,
                strikeoutPosition * scale
            );
        }

        public Glyph GetGlyph (CodePoint codePoint, float pixelSize) {
            var glyphIndex = charMap.Lookup(codePoint);
            if (glyphIndex < 0)
                return null;

            // get horizontal metrics
            var horizontal = hmetrics[glyphIndex];

            //  get vertical metrics if we have them; otherwise synthesize them
            // TODO:
            
            // build and transform the glyph
            var points = new List<PointF>(32);
            var contours = new List<int>(32);
            var transform = Matrix3x2.CreateScale(ComputeScale(pixelSize));
            ComposeGlyphs(glyphs[glyphIndex], 0, ref transform, points, contours);

            return new Glyph(renderer, points.ToArray(), contours.ToArray());

            //var glyphData = glyphs[glyphIndex];
            //var outline = glyphData.Outline;
            //var points = outline.Points;
            //// TODO: don't round the control box
            //var cbox = FixedMath.ComputeControlBox(points);

            //return new Glyph(
            //    glyphData,
            //    renderer,
            //    (int)cbox.MinX * scale,
            //    (int)cbox.MaxY * scale,
            //    (int)(cbox.MaxX - cbox.MinX) * scale,
            //    (int)(cbox.MaxY - cbox.MinY) * scale,
            //    horizontal.Advance * scale
            //);
        }

        void ComposeGlyphs (BaseGlyph glyph, int startPoint, ref Matrix3x2 transform, List<PointF> basePoints, List<int> baseContours) {
            var simple = glyph as SimpleGlyph;
            if (simple != null) {
                baseContours.AddRange(simple.Outline.ContourEndpoints);
                foreach (var point in simple.Outline.Points)
                    basePoints.Add(new PointF(Vector2.TransformNormal((Vector2)point, transform), point.Type));
            }
            else {
                // otherwise, we have a composite glyph
                var composite = (CompositeGlyph)glyph;
                foreach (var subglyph in composite.Subglyphs) {
                    // if we have a scale, update the local transform
                    var local = transform;
                    bool haveScale = (subglyph.Flags & (CompositeGlyphFlags.HaveScale | CompositeGlyphFlags.HaveXYScale | CompositeGlyphFlags.HaveTransform)) != 0;
                    if (haveScale)
                        local = transform * subglyph.Transform;

                    // recursively compose the subglyph into our lists
                    int currentPoints = basePoints.Count;
                    ComposeGlyphs(glyphs[subglyph.Index], currentPoints, ref local, basePoints, baseContours);

                    // calculate the offset for the subglyph. we have to do offsetting after composing all subglyphs,
                    // because we might need to find the offset based on previously composed points by index
                    Vector2 offset;
                    if ((subglyph.Flags & CompositeGlyphFlags.ArgsAreXYValues) != 0) {
                        offset = (Vector2)new Point((FUnit)subglyph.Arg1, (FUnit)subglyph.Arg2);
                        if (haveScale && (subglyph.Flags & CompositeGlyphFlags.ScaledComponentOffset) != 0)
                            offset = Vector2.TransformNormal(offset, local);
                        else
                            offset = Vector2.TransformNormal(offset, transform);

                        // if the RoundXYToGrid flag is set, round the offset components
                        if ((subglyph.Flags & CompositeGlyphFlags.RoundXYToGrid) != 0)
                            offset = new Vector2((float)Math.Round(offset.X), (float)Math.Round(offset.Y));
                    }
                    else {
                        // if the offsets are not given in FUnits, then they are point indices
                        // in the currently composed base glyph that we should match up
                        var p1 = basePoints[subglyph.Arg1 + startPoint];
                        var p2 = basePoints[subglyph.Arg2 + currentPoints];
                        offset = p1.P - p2.P;
                    }

                    // translate all child points
                    if (offset != Vector2.Zero) {
                        //for (int i = currentPoints; i < basePoints.Count; i++)
                          //  basePoints[i].Offset(offset);
                    }
                }
            }
        }

        float ComputeScale (float pixelSize) {
            if (integerPpems)
                pixelSize = (float)Math.Round(pixelSize, MidpointRounding.AwayFromZero);
            return pixelSize / unitsPerEm;
        }
    }

    public struct CodePoint {
        int value;

        public CodePoint (int codePoint) {
            value = codePoint;
        }

        public CodePoint (char character) {
            value = character;
        }

        public CodePoint (char highSurrogate, char lowSurrogate) {
            value = char.ConvertToUtf32(highSurrogate, lowSurrogate);
        }

        public override string ToString () => $"{value} ({(char)value})";

        public static explicit operator CodePoint (int codePoint) => new CodePoint(codePoint);
        public static implicit operator CodePoint (char character) => new CodePoint(character);
    }

    public class FaceMetrics {
        public readonly float CellAscent;
        public readonly float CellDescent;
        public readonly float LineHeight;
        public readonly float XHeight;
        public readonly float CapHeight;
        public readonly float UnderlineSize;
        public readonly float UnderlinePosition;
        public readonly float StrikeoutSize;
        public readonly float StrikeoutPosition;

        public FaceMetrics (
            float cellAscent, float cellDescent, float lineHeight, float xHeight,
            float capHeight, float underlineSize, float underlinePosition,
            float strikeoutSize, float strikeoutPosition
        ) {
            CellAscent = cellAscent;
            CellDescent = cellDescent;
            LineHeight = lineHeight;
            XHeight = xHeight;
            CapHeight = capHeight;
            UnderlineSize = underlineSize;
            UnderlinePosition = underlinePosition;
            StrikeoutSize = strikeoutSize;
            StrikeoutPosition = strikeoutPosition;
        }
    }

    public enum FontWeight {
        Unknown = 0,
        Thin = 100,
        ExtraLight = 200,
        Light = 300,
        Normal = 400,
        Medium = 500,
        SemiBold = 600,
        Bold = 700,
        ExtraBold = 800,
        Black = 900
    }

    public enum FontStretch {
        Unknown,
        UltraCondensed,
        ExtraCondensed,
        Condensed,
        SemiCondensed,
        Normal,
        SemiExpanded,
        Expanded,
        ExtraExpanded,
        UltraExpanded
    }

    public enum FontStyle {
        Regular,
        Bold,
        Italic,
        Oblique
    }
}