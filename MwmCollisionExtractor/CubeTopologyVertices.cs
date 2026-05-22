using System;
using VRageMath;

namespace MwmCollisionExtractor
{
    public enum CubeTopology
    {
        Slope, RotatedSlope, RoundSlope, Corner, RotatedCorner, RoundCorner,
        InvCorner, RoundInvCorner, Box, RoundedSlope, StandaloneBox,
        Slope2Base, Slope2Tip, Corner2Base, Corner2Tip, InvCorner2Base, InvCorner2Tip,
        HalfBox, HalfSlopeBox, HalfSlopeInverted, HalfSlopeCorner, HalfSlopeCornerInverted,
        SlopedCornerTip, SlopedCornerBase, SlopedCorner, HalfSlopedCornerBase, HalfCorner,
        CornerSquare, CornerSquareInverted, HalfSlopedCorner, RaisedSlopedCorner,
        SlopeTransition, SlopeTransitionBase, SlopeTransitionBaseMirrored, SlopeTransitionMirrored,
        SlopeTransitionTip, SlopeTransitionTipMirrored, SquareSlopedCornerBase,
        SquareSlopedCornerTip, SquareSlopedCornerTipInv
    }

    public static class CubeTopologyVertices
    {
        public static Vector3[] GetVertices(CubeTopology topology)
        {
            switch (topology)
            {
                case CubeTopology.Slope:
                case CubeTopology.RotatedSlope:
                    return new[]
                    {
                        new Vector3(-1, 1, -1), new Vector3(1, 1, -1),
                        new Vector3(1, -1, 1), new Vector3(-1, -1, 1),
                        new Vector3(-1, -1, -1), new Vector3(1, -1, -1),
                        new Vector3(-1, 0, 0), new Vector3(-1, 0, -1),
                        new Vector3(-1, -1, 0), new Vector3(0, 0, 0),
                        new Vector3(0, 0, -1), new Vector3(0, -1, 0),
                        new Vector3(1, 0, 0), new Vector3(1, 0, -1),
                        new Vector3(1, -1, 0)
                    };

                case CubeTopology.RoundSlope:
                    return new[]
                    {
                        new Vector3(-1, 1, -1), new Vector3(1, 1, -1),
                        new Vector3(1, -1, 1), new Vector3(-1, -1, 1),
                        new Vector3(-1, -1, -1), new Vector3(1, -1, -1),
                        new Vector3(-1f, 0.414f, 0.414f), new Vector3(1f, 0.414f, 0.414f),
                        new Vector3(-1, 0, 0), new Vector3(-1, 0, -1),
                        new Vector3(-1, -1, 0), new Vector3(0, 0, 0),
                        new Vector3(0, 0, -1), new Vector3(0, -1, 0),
                        new Vector3(1, 0, 0), new Vector3(1, 0, -1),
                        new Vector3(1, -1, 0)
                    };

                case CubeTopology.Corner:
                case CubeTopology.RotatedCorner:
                    return new[]
                    {
                        new Vector3(1, 1, -1), new Vector3(1, -1, -1),
                        new Vector3(-1, -1, -1), new Vector3(1, -1, 1),
                        new Vector3(0, -1, 0), new Vector3(1, -1, 0),
                        new Vector3(0, -1, -1), new Vector3(1, 0, -1),
                        new Vector3(1, 0, 0), new Vector3(0, 0, -1)
                    };

                case CubeTopology.RoundCorner:
                    return new[]
                    {
                        new Vector3(1, 1, -1), new Vector3(1, -1, -1),
                        new Vector3(-1, -1, -1), new Vector3(1, -1, 1),
                        new Vector3(-0.414f, 0.414f, -1f), new Vector3(-0.414f, -1f, 0.414f),
                        new Vector3(1f, 0.414f, 0.414f),
                        new Vector3(0, -1, 0), new Vector3(1, -1, 0),
                        new Vector3(0, -1, -1), new Vector3(1, 0, -1),
                        new Vector3(1, 0, 0), new Vector3(0, 0, -1)
                    };

                case CubeTopology.InvCorner:
                    return new[]
                    {
                        new Vector3(1, 1, 1), new Vector3(1, 1, -1),
                        new Vector3(1, -1, 1), new Vector3(-1, 1, 1),
                        new Vector3(-1, 1, -1), new Vector3(-1, -1, 1),
                        new Vector3(-1, -1, -1),
                        new Vector3(-1, -1, 0), new Vector3(-1, 0, -1),
                        new Vector3(-1, 0, 0), new Vector3(-1, 0, 1),
                        new Vector3(-1, 1, 0), new Vector3(0, -1, 0),
                        new Vector3(0, -1, 1), new Vector3(0, 0, -1),
                        new Vector3(0, 0, 0), new Vector3(0, 0, 1),
                        new Vector3(0, 1, -1), new Vector3(0, 1, 0),
                        new Vector3(0, 1, 1), new Vector3(1, 0, 0),
                        new Vector3(1, 0, 1), new Vector3(1, 1, 0)
                    };

                case CubeTopology.RoundInvCorner:
                    return new[]
                    {
                        new Vector3(1, 1, 1), new Vector3(1, 1, -1),
                        new Vector3(1, -1, 1), new Vector3(-1, 1, 1),
                        new Vector3(-1, 1, -1), new Vector3(-1, -1, 1),
                        new Vector3(-1, -1, -1),
                        new Vector3(0.414f, -0.414f, -1f), new Vector3(0.414f, -1f, -0.414f),
                        new Vector3(1f, -0.414f, -0.414f),
                        new Vector3(-1, -1, 0), new Vector3(-1, 0, -1),
                        new Vector3(-1, 0, 0), new Vector3(-1, 0, 1),
                        new Vector3(-1, 1, 0), new Vector3(0, -1, 0),
                        new Vector3(0, -1, 1), new Vector3(0, 0, -1),
                        new Vector3(0, 0, 0), new Vector3(0, 0, 1),
                        new Vector3(0, 1, -1), new Vector3(0, 1, 0),
                        new Vector3(0, 1, 1), new Vector3(1, 0, 0),
                        new Vector3(1, 0, 1), new Vector3(1, 1, 0)
                    };

                case CubeTopology.Box:
                case CubeTopology.RoundedSlope:
                    return new[]
                    {
                        new Vector3(1, 1, 1), new Vector3(1, 1, -1),
                        new Vector3(1, -1, 1), new Vector3(1, -1, -1),
                        new Vector3(-1, 1, 1), new Vector3(-1, 1, -1),
                        new Vector3(-1, -1, 1), new Vector3(-1, -1, -1),
                        new Vector3(-1, -1, 0), new Vector3(-1, 0, -1),
                        new Vector3(-1, 0, 0), new Vector3(-1, 0, 1),
                        new Vector3(-1, 1, 0), new Vector3(0, -1, -1),
                        new Vector3(0, -1, 0), new Vector3(0, -1, 1),
                        new Vector3(0, 0, -1), new Vector3(0, 0, 1),
                        new Vector3(0, 1, -1), new Vector3(0, 1, 0),
                        new Vector3(0, 1, 1), new Vector3(1, -1, 0),
                        new Vector3(1, 0, -1), new Vector3(1, 0, 0),
                        new Vector3(1, 0, 1), new Vector3(1, 1, 0)
                    };

                case CubeTopology.Slope2Base:
                    return new[]
                    {
                        new Vector3(1, 0, 1), new Vector3(1, 1, -1),
                        new Vector3(1, -1, 1), new Vector3(1, -1, -1),
                        new Vector3(-1, 0, 1), new Vector3(-1, 1, -1),
                        new Vector3(-1, -1, 1), new Vector3(-1, -1, -1),
                        new Vector3(-1, -1, 0), new Vector3(-1, 0, -1),
                        new Vector3(-1, 0, 0), new Vector3(-1, 0, 1),
                        new Vector3(-1, 0.5f, 0), new Vector3(0, -1, -1),
                        new Vector3(0, -1, 0), new Vector3(0, -1, 1),
                        new Vector3(0, 0, -1), new Vector3(0, 0, 0),
                        new Vector3(0, 0, 1), new Vector3(0, 1, -1),
                        new Vector3(0, 0.5f, 0), new Vector3(0, 0, 1),
                        new Vector3(1, -1, 0), new Vector3(1, 0, -1),
                        new Vector3(1, 0, 0), new Vector3(1, 0, 1),
                        new Vector3(1, 0.5f, 0)
                    };

                case CubeTopology.Slope2Tip:
                    return new[]
                    {
                        new Vector3(-1, 0, -1), new Vector3(1, 0, -1),
                        new Vector3(1, -1, 1), new Vector3(-1, -1, 1),
                        new Vector3(-1, -1, -1), new Vector3(1, -1, -1),
                        new Vector3(-1, -0.5f, 0), new Vector3(-1, 0, -1),
                        new Vector3(-1, -1, 0), new Vector3(0, -0.5f, 0),
                        new Vector3(0, 0, -1), new Vector3(0, -1, 0),
                        new Vector3(1, -0.5f, 0), new Vector3(1, 0, -1),
                        new Vector3(1, -1, 0)
                    };

                case CubeTopology.Corner2Base:
                    return new[]
                    {
                        new Vector3(-1, 1, -1), new Vector3(1, 0, -1),
                        new Vector3(1, -1, 0), new Vector3(-1, -1, 1),
                        new Vector3(-1, -1, -1), new Vector3(1, -1, -1),
                        new Vector3(-1, 0, 0), new Vector3(-1, 0, -1),
                        new Vector3(-1, -1, 0), new Vector3(0.5f, -0.5f, 0),
                        new Vector3(0, 0, -1), new Vector3(0, -1, 0),
                        new Vector3(1, -0.5f, -0.5f), new Vector3(1, 0, -1),
                        new Vector3(1, -1, 0)
                    };

                case CubeTopology.Corner2Tip:
                    return new[]
                    {
                        new Vector3(1, 0, -1), new Vector3(1, -1, -1),
                        new Vector3(0, -1, -1), new Vector3(1, -1, 1),
                        new Vector3(0.5f, -1, 0), new Vector3(1, -1, 0),
                        new Vector3(0, -1, -1), new Vector3(1, 0, -1),
                        new Vector3(1, -0.5f, 0), new Vector3(0.5f, -0.5f, -1)
                    };

                case CubeTopology.InvCorner2Base:
                    return new[]
                    {
                        new Vector3(1, 1, 1), new Vector3(1, 1, -1),
                        new Vector3(1, -1, 1), new Vector3(1, 0, -1),
                        new Vector3(0, -1, -1), new Vector3(-1, 1, 1),
                        new Vector3(-1, 1, -1), new Vector3(-1, -1, 1),
                        new Vector3(-1, -1, -1),
                        new Vector3(-1, -1, 0), new Vector3(-1, 0, -1),
                        new Vector3(-1, 0, 0), new Vector3(-1, 0, 1),
                        new Vector3(-1, 1, 0), new Vector3(0, -1, 0),
                        new Vector3(0, -1, 1), new Vector3(0, 0, -1),
                        new Vector3(0, 0, 0), new Vector3(0, 0, 1),
                        new Vector3(0, 1, -1), new Vector3(0, 1, 0),
                        new Vector3(0, 1, 1), new Vector3(1, 0, 0),
                        new Vector3(1, 0, 1), new Vector3(1, 1, 0)
                    };

                case CubeTopology.InvCorner2Tip:
                    return new[]
                    {
                        new Vector3(1, 1, 1), new Vector3(1, 1, -1),
                        new Vector3(-1, 1, 1), new Vector3(-1, 1, -1),
                        new Vector3(-1, -1, 1), new Vector3(-1, -1, -1),
                        new Vector3(-1, -1, 0), new Vector3(-1, 0, -1),
                        new Vector3(-1, 0, 0), new Vector3(-1, 0, 1),
                        new Vector3(-1, 1, 0), new Vector3(0, -1, 0),
                        new Vector3(0, -1, 1), new Vector3(0, 0, -1),
                        new Vector3(0, 0, 0), new Vector3(0, 0, 1),
                        new Vector3(0, 1, -1), new Vector3(0, 1, 0),
                        new Vector3(0, 1, 1), new Vector3(1, 0, 0),
                        new Vector3(1, 0, 1), new Vector3(1, 1, 0)
                    };

                case CubeTopology.StandaloneBox:
                    return Array.Empty<Vector3>();

                case CubeTopology.HalfBox:
                    return new[]
                    {
                        new Vector3(1, 1, 0), new Vector3(1, -1, 0),
                        new Vector3(1, 1, -1), new Vector3(1, -1, -1),
                        new Vector3(-1, 1, 0), new Vector3(-1, -1, 0),
                        new Vector3(-1, 1, -1), new Vector3(-1, -1, -1)
                    };

                case CubeTopology.HalfSlopeBox:
                    return new[]
                    {
                        new Vector3(-1, 0, -1), new Vector3(1, 0, -1),
                        new Vector3(-1, -1, 0), new Vector3(1, -1, 0),
                        new Vector3(-1, -1, -1), new Vector3(1, -1, -1)
                    };

                case CubeTopology.HalfSlopeInverted:
                    return new[]
                    {
                        new Vector3(-1, -1, 1), new Vector3(-1, 0, 1),
                        new Vector3(-1, -1, 0), new Vector3(-1, -1, -1),
                        new Vector3(-1, 0, -1), new Vector3(-1, 1, 1),
                        new Vector3(-1, 1, 0), new Vector3(-1, 1, -1),
                        new Vector3(1, -1, 1), new Vector3(0, -1, 1),
                        new Vector3(1, -1, 0), new Vector3(1, -1, -1),
                        new Vector3(0, -1, -1), new Vector3(0, 1, 1),
                        new Vector3(1, 0, 1), new Vector3(0, 1, -1),
                        new Vector3(1, 0, -1)
                    };

                case CubeTopology.HalfSlopeCorner:
                    return new[]
                    {
                        new Vector3(1, -1, 1), new Vector3(0, -1, 1),
                        new Vector3(1, -1, 0), new Vector3(1, 0, 1)
                    };

                case CubeTopology.HalfSlopeCornerInverted:
                    return new[]
                    {
                        new Vector3(0, 1, -1), new Vector3(-1, 0, -1),
                        new Vector3(-1, -1, -1), new Vector3(0, -1, -1),
                        new Vector3(1, 1, -1), new Vector3(1, 0, -1),
                        new Vector3(1, -1, -1), new Vector3(-0.5f, 0.5f, -1),
                        new Vector3(-1, 1, 0), new Vector3(-1, -1, 0),
                        new Vector3(-1, 1, 1), new Vector3(-1, 0, 1),
                        new Vector3(-1, -1, 1), new Vector3(-1, 0.5f, -0.5f),
                        new Vector3(1, 1, 0), new Vector3(0, 1, 1),
                        new Vector3(1, 1, 1), new Vector3(-0.5f, 1, -0.5f),
                        new Vector3(1, -1, 1), new Vector3(0, -1, 1),
                        new Vector3(1, -1, 0), new Vector3(1, 0, 1)
                    };

                case CubeTopology.HalfSlopedCorner:
                    return new[]
                    {
                        new Vector3(1, 0, -1), new Vector3(1, -1, -1),
                        new Vector3(-1, -1, 1), new Vector3(-1, 0, 1),
                        new Vector3(0, -1, 0), new Vector3(0, 0, 0),
                        new Vector3(-1, 1, -1), new Vector3(0, 0.5f, -1),
                        new Vector3(-1, 0.5f, 0), new Vector3(-1, -1, -1),
                        new Vector3(-1, 0, -1), new Vector3(0, -1, -1),
                        new Vector3(-1, -1, 0)
                    };

                case CubeTopology.HalfSlopedCornerBase:
                    return new[]
                    {
                        new Vector3(1, -1, 1), new Vector3(-1, -1, 1),
                        new Vector3(-1, 0, 1), new Vector3(0, -0.5f, 1),
                        new Vector3(0, -1, 1), new Vector3(1, 0, -1),
                        new Vector3(1, -0.5f, 0), new Vector3(0, 0, 0),
                        new Vector3(-1, 0, -1), new Vector3(-1, 0, 0),
                        new Vector3(0, 0, -1), new Vector3(-1, -1, -1),
                        new Vector3(0, -1, -1), new Vector3(1, -1, -1),
                        new Vector3(1, -1, 0), new Vector3(-1, -1, 0)
                    };

                case CubeTopology.HalfCorner:
                    return new[]
                    {
                        new Vector3(-1, 0, -1), new Vector3(1, 0, -1),
                        new Vector3(-1, 0, 1), new Vector3(-1, 0, 0),
                        new Vector3(0, 0, 0), new Vector3(0, 0, -1),
                        new Vector3(1, -1, -1), new Vector3(-1, -1, 1),
                        new Vector3(0, -1, 0), new Vector3(-1, -1, -1),
                        new Vector3(0, -1, -1), new Vector3(-1, -1, 0)
                    };

                case CubeTopology.CornerSquare:
                    return new[]
                    {
                        new Vector3(-1, -1, -1), new Vector3(0, -1, -1),
                        new Vector3(1, -1, -1), new Vector3(-1, 0, -1),
                        new Vector3(0, 0, -1), new Vector3(-1, 1, -1),
                        new Vector3(-1, -1, 0), new Vector3(-1, -1, 1),
                        new Vector3(-1, 0, 0), new Vector3(1, -1, 1),
                        new Vector3(0, 0, 0), new Vector3(1, -1, 0),
                        new Vector3(0, -1, 1)
                    };

                case CubeTopology.CornerSquareInverted:
                    return new[]
                    {
                        new Vector3(1, -1, -1), new Vector3(1, -1, 0),
                        new Vector3(1, -1, 1), new Vector3(1, 0, -1),
                        new Vector3(1, 0, 0), new Vector3(1, 1, -1),
                        new Vector3(-1, -1, 1), new Vector3(-1, 0, 1),
                        new Vector3(-1, -1, 0), new Vector3(-1, -1, -1),
                        new Vector3(-1, 0, -1), new Vector3(-1, 1, 1),
                        new Vector3(-1, 1, 0), new Vector3(-1, 1, -1),
                        new Vector3(0, -1, 1), new Vector3(0, 0, 1),
                        new Vector3(0, -1, -1), new Vector3(0, 1, -1),
                        new Vector3(0, 0, 0)
                    };

                case CubeTopology.SlopedCorner:
                    return new[]
                    {
                        new Vector3(-1, 1, -1), new Vector3(1, 0, -1),
                        new Vector3(-1, 0, 1), new Vector3(1, -1, 1),
                        new Vector3(1, -0.5f, 0), new Vector3(0, 0.5f, -1),
                        new Vector3(-1, 0.5f, 0), new Vector3(0, -0.5f, 1),
                        new Vector3(-1, -1, 1), new Vector3(-1, -1, 0),
                        new Vector3(-1, -1, -1), new Vector3(-1, 0, -1),
                        new Vector3(1, -1, -1), new Vector3(1, -1, 0),
                        new Vector3(0, -1, 1), new Vector3(0, -1, -1)
                    };

                case CubeTopology.SlopedCornerBase:
                    return new[]
                    {
                        new Vector3(1, 1, -1), new Vector3(0, 1, -1),
                        new Vector3(-1, 1, -1), new Vector3(1, 1, 0),
                        new Vector3(0, 1, 0), new Vector3(1, 1, 1),
                        new Vector3(-1, 0, 1), new Vector3(-1, 0.5f, 0),
                        new Vector3(0, 0.5f, 1), new Vector3(1, 0, -1),
                        new Vector3(1, -1, -1), new Vector3(1, -1, 0),
                        new Vector3(1, 0, 1), new Vector3(1, -1, 1),
                        new Vector3(-1, -1, 1), new Vector3(-1, -1, 0),
                        new Vector3(-1, -1, -1), new Vector3(-1, 0, -1),
                        new Vector3(0, -1, -1), new Vector3(0, -1, 1)
                    };

                case CubeTopology.SlopedCornerTip:
                    return new[]
                    {
                        new Vector3(1, -1, 1), new Vector3(-1, -1, 1),
                        new Vector3(-1, 0, 1), new Vector3(0, -0.5f, 1),
                        new Vector3(-1, -0.5f, 1), new Vector3(0, -1, 1),
                        new Vector3(-1, -1, 0), new Vector3(-1, -1, -1),
                        new Vector3(0, -1, 0), new Vector3(-1, -0.5f, 0)
                    };

                case CubeTopology.RaisedSlopedCorner:
                    return new[]
                    {
                        new Vector3(1, 0, -1), new Vector3(-1, 0, 1),
                        new Vector3(-1, 1, -1), new Vector3(-1, 0, -1),
                        new Vector3(1, 0, -1), new Vector3(-1, 0, 1),
                        new Vector3(-1, -1, 1), new Vector3(-1, -1, -1),
                        new Vector3(-1, 0, -1), new Vector3(1, 0, 1),
                        new Vector3(1, -1, 1), new Vector3(1, -1, -1)
                    };

                case CubeTopology.SlopeTransition:
                    return new[]
                    {
                        new Vector3(1, -1, -1), new Vector3(1, -1, 0),
                        new Vector3(1, -1, 1), new Vector3(1, -0.5f, 0),
                        new Vector3(1, 0, -1), new Vector3(-1, -1, -1),
                        new Vector3(-1, -1, 0), new Vector3(-1, -1, 1),
                        new Vector3(-1, 0, -1), new Vector3(-1, 0, 0),
                        new Vector3(-1, 1, -1), new Vector3(0, -1, -1),
                        new Vector3(0, 0.5f, -1), new Vector3(0, -1, 1),
                        new Vector3(0, 0, 0)
                    };

                case CubeTopology.SlopeTransitionBase:
                    return new[]
                    {
                        new Vector3(-1, -1, 1), new Vector3(0, -1, 1),
                        new Vector3(1, -1, 1), new Vector3(0, -0.5f, 1),
                        new Vector3(-1, 0, 1), new Vector3(-1, 1, -1),
                        new Vector3(0, 1, -1), new Vector3(-1, 0, -1),
                        new Vector3(-1, -1, -1), new Vector3(0, -1, -1),
                        new Vector3(1, 1, -1), new Vector3(1, 0, -1),
                        new Vector3(1, -1, -1), new Vector3(1, 0, 0),
                        new Vector3(0, 0.5f, 0), new Vector3(-1, -1, 0),
                        new Vector3(1, -1, 0), new Vector3(-1, 0.5f, 0)
                    };

                case CubeTopology.SlopeTransitionBaseMirrored:
                    return new[]
                    {
                        new Vector3(1, -1, -1), new Vector3(-1, 0, -1),
                        new Vector3(1, 1, 1), new Vector3(0, -0.5f, -1),
                        new Vector3(1, 0, 0), new Vector3(0, 0.5f, 0),
                        new Vector3(-1, -1, -1), new Vector3(0, -1, -1),
                        new Vector3(-1, -1, 1), new Vector3(-1, 0, 1),
                        new Vector3(-1, -1, 0), new Vector3(-1, 1, 1),
                        new Vector3(-1, 0.5f, 0), new Vector3(0, 1, 1),
                        new Vector3(0, -1, 1), new Vector3(1, -1, 0),
                        new Vector3(1, -1, 1), new Vector3(1, 0, 1)
                    };

                case CubeTopology.SlopeTransitionMirrored:
                    return new[]
                    {
                        new Vector3(-1, -1, 1), new Vector3(-1, -1, 0),
                        new Vector3(-1, -1, -1), new Vector3(-1, 0, 1),
                        new Vector3(-1, 0, 0), new Vector3(-1, 1, 1),
                        new Vector3(1, -1, 1), new Vector3(1, -1, 0),
                        new Vector3(1, -1, -1), new Vector3(1, -0.5f, 0),
                        new Vector3(1, 0, 1), new Vector3(0, -1, -1),
                        new Vector3(0, 0, 0), new Vector3(0, 0.5f, 1),
                        new Vector3(0, -1, 1)
                    };

                case CubeTopology.SlopeTransitionTip:
                    return new[]
                    {
                        new Vector3(-1, 0, -1), new Vector3(0, 0, -1),
                        new Vector3(-1, -1, -1), new Vector3(0, -1, -1),
                        new Vector3(1, 0, -1), new Vector3(1, -1, -1),
                        new Vector3(-1, -1, 1), new Vector3(1, -1, 0),
                        new Vector3(0, -0.5f, 0), new Vector3(0, -1, 0.5f),
                        new Vector3(1, -0.5f, -0.5f), new Vector3(-1, -1, 0),
                        new Vector3(-1, -0.5f, 0)
                    };

                case CubeTopology.SlopeTransitionTipMirrored:
                    return new[]
                    {
                        new Vector3(-1, -1, 1), new Vector3(-1, -1, 0),
                        new Vector3(-1, -1, -1), new Vector3(-1, -0.5f, 0),
                        new Vector3(-1, 0, 1), new Vector3(1, 0, 1),
                        new Vector3(1, -1, 0), new Vector3(0, -0.5f, 0),
                        new Vector3(0, -1, -0.5f), new Vector3(1, -0.5f, 0.5f),
                        new Vector3(0, 0, 1), new Vector3(1, -1, 1),
                        new Vector3(0, -1, 1)
                    };

                case CubeTopology.SquareSlopedCornerBase:
                    return new[]
                    {
                        new Vector3(-1, -1, 1), new Vector3(-1, 0, 1),
                        new Vector3(-1, -1, 0), new Vector3(-1, -1, -1),
                        new Vector3(-1, 0, -1), new Vector3(-1, 0.5f, 0),
                        new Vector3(-1, 1, -1), new Vector3(0, -1, -1),
                        new Vector3(0, -1, 1), new Vector3(1, -1, -1),
                        new Vector3(1, -1, 0), new Vector3(1, -1, 1),
                        new Vector3(1, 0, -1), new Vector3(1, 0, 1),
                        new Vector3(0, 0.5f, -1), new Vector3(0, 0.5f, 0),
                        new Vector3(1, 0, 0), new Vector3(0, 0, 1)
                    };

                case CubeTopology.SquareSlopedCornerTip:
                    return new[]
                    {
                        new Vector3(-1, -1, -1), new Vector3(1, -1, -1),
                        new Vector3(-1, 0, -1), new Vector3(0, -0.5f, -1),
                        new Vector3(0, -1, -1), new Vector3(-1, -0.5f, -1),
                        new Vector3(-1, -1, 1), new Vector3(1, -1, 1),
                        new Vector3(0, -1, 1), new Vector3(0, -0.5f, 0),
                        new Vector3(-1, -0.5f, 0), new Vector3(1, -1, 0),
                        new Vector3(-1, -1, 0)
                    };

                case CubeTopology.SquareSlopedCornerTipInv:
                    return new[]
                    {
                        new Vector3(1, -1, -1), new Vector3(1, -1, 1),
                        new Vector3(1, 0, -1), new Vector3(-1, 0, -1),
                        new Vector3(-1, -1, -1), new Vector3(-1, -1, -1),
                        new Vector3(-1, 0, -1), new Vector3(1, -1, 1),
                        new Vector3(-1, 0, 1), new Vector3(-1, -1, 1),
                        new Vector3(-1, -1, -1)
                    };

                default:
                    return Array.Empty<Vector3>();
            }
        }
    }
}
