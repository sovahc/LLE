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
			return topology switch
			{
				CubeTopology.Slope or CubeTopology.RotatedSlope => [
						new Vector3(-1, 1, -1), new Vector3(1, 1, -1),
						new Vector3(1, -1, 1), new Vector3(-1, -1, 1),
						new Vector3(-1, -1, -1), new Vector3(1, -1, -1),
						new Vector3(-1, 0, 0), new Vector3(-1, 0, -1),
						new Vector3(-1, -1, 0), new Vector3(0, 0, 0),
						new Vector3(0, 0, -1), new Vector3(0, -1, 0),
						new Vector3(1, 0, 0), new Vector3(1, 0, -1),
						new Vector3(1, -1, 0)
									],
				CubeTopology.RoundSlope => [
						new Vector3(-1, 1, -1), new Vector3(1, 1, -1),
						new Vector3(1, -1, 1), new Vector3(-1, -1, 1),
						new Vector3(-1, -1, -1), new Vector3(1, -1, -1),
						new Vector3(-1f, 0.414f, 0.414f), new Vector3(1f, 0.414f, 0.414f),
						new Vector3(-1, 0, 0), new Vector3(-1, 0, -1),
						new Vector3(-1, -1, 0), new Vector3(0, 0, 0),
						new Vector3(0, 0, -1), new Vector3(0, -1, 0),
						new Vector3(1, 0, 0), new Vector3(1, 0, -1),
						new Vector3(1, -1, 0)
									],
				CubeTopology.Corner or CubeTopology.RotatedCorner => [
						new Vector3(1, 1, -1), new Vector3(1, -1, -1),
						new Vector3(-1, -1, -1), new Vector3(1, -1, 1),
						new Vector3(0, -1, 0), new Vector3(1, -1, 0),
						new Vector3(0, -1, -1), new Vector3(1, 0, -1),
						new Vector3(1, 0, 0), new Vector3(0, 0, -1)
									],
				CubeTopology.RoundCorner => [
						new Vector3(1, 1, -1), new Vector3(1, -1, -1),
						new Vector3(-1, -1, -1), new Vector3(1, -1, 1),
						new Vector3(-0.414f, 0.414f, -1f), new Vector3(-0.414f, -1f, 0.414f),
						new Vector3(1f, 0.414f, 0.414f),
						new Vector3(0, -1, 0), new Vector3(1, -1, 0),
						new Vector3(0, -1, -1), new Vector3(1, 0, -1),
						new Vector3(1, 0, 0), new Vector3(0, 0, -1)
									],
				CubeTopology.InvCorner => [
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
									],
				CubeTopology.RoundInvCorner => [
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
									],
				CubeTopology.Box or CubeTopology.RoundedSlope => [
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
									],
				CubeTopology.Slope2Base => [
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
									],
				CubeTopology.Slope2Tip => [
						new Vector3(-1, 0, -1), new Vector3(1, 0, -1),
						new Vector3(1, -1, 1), new Vector3(-1, -1, 1),
						new Vector3(-1, -1, -1), new Vector3(1, -1, -1),
						new Vector3(-1, -0.5f, 0), new Vector3(-1, 0, -1),
						new Vector3(-1, -1, 0), new Vector3(0, -0.5f, 0),
						new Vector3(0, 0, -1), new Vector3(0, -1, 0),
						new Vector3(1, -0.5f, 0), new Vector3(1, 0, -1),
						new Vector3(1, -1, 0)
									],
				CubeTopology.Corner2Base => [
						new Vector3(-1, 1, -1), new Vector3(1, 0, -1),
						new Vector3(1, -1, 0), new Vector3(-1, -1, 1),
						new Vector3(-1, -1, -1), new Vector3(1, -1, -1),
						new Vector3(-1, 0, 0), new Vector3(-1, 0, -1),
						new Vector3(-1, -1, 0), new Vector3(0.5f, -0.5f, 0),
						new Vector3(0, 0, -1), new Vector3(0, -1, 0),
						new Vector3(1, -0.5f, -0.5f), new Vector3(1, 0, -1),
						new Vector3(1, -1, 0)
									],
				CubeTopology.Corner2Tip => [
						new Vector3(1, 0, -1), new Vector3(1, -1, -1),
						new Vector3(0, -1, -1), new Vector3(1, -1, 1),
						new Vector3(0.5f, -1, 0), new Vector3(1, -1, 0),
						new Vector3(0, -1, -1), new Vector3(1, 0, -1),
						new Vector3(1, -0.5f, 0), new Vector3(0.5f, -0.5f, -1)
									],
				CubeTopology.InvCorner2Base => [
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
									],
				CubeTopology.InvCorner2Tip => [
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
									],
				CubeTopology.StandaloneBox => [],
				CubeTopology.HalfBox => [
						new Vector3(1, 1, 0), new Vector3(1, -1, 0),
						new Vector3(1, 1, -1), new Vector3(1, -1, -1),
						new Vector3(-1, 1, 0), new Vector3(-1, -1, 0),
						new Vector3(-1, 1, -1), new Vector3(-1, -1, -1)
									],
				CubeTopology.HalfSlopeBox => [
						new Vector3(-1, 0, -1), new Vector3(1, 0, -1),
						new Vector3(-1, -1, 0), new Vector3(1, -1, 0),
						new Vector3(-1, -1, -1), new Vector3(1, -1, -1)
									],
				CubeTopology.HalfSlopeInverted => [
						new Vector3(-1, -1, 1), new Vector3(-1, 0, 1),
						new Vector3(-1, -1, 0), new Vector3(-1, -1, -1),
						new Vector3(-1, 0, -1), new Vector3(-1, 1, 1),
						new Vector3(-1, 1, 0), new Vector3(-1, 1, -1),
						new Vector3(1, -1, 1), new Vector3(0, -1, 1),
						new Vector3(1, -1, 0), new Vector3(1, -1, -1),
						new Vector3(0, -1, -1), new Vector3(0, 1, 1),
						new Vector3(1, 0, 1), new Vector3(0, 1, -1),
						new Vector3(1, 0, -1)
									],
				CubeTopology.HalfSlopeCorner => [
						new Vector3(1, -1, 1), new Vector3(0, -1, 1),
						new Vector3(1, -1, 0), new Vector3(1, 0, 1)
									],
				CubeTopology.HalfSlopeCornerInverted => [
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
									],
				CubeTopology.HalfSlopedCorner => [
						new Vector3(1, 0, -1), new Vector3(1, -1, -1),
						new Vector3(-1, -1, 1), new Vector3(-1, 0, 1),
						new Vector3(0, -1, 0), new Vector3(0, 0, 0),
						new Vector3(-1, 1, -1), new Vector3(0, 0.5f, -1),
						new Vector3(-1, 0.5f, 0), new Vector3(-1, -1, -1),
						new Vector3(-1, 0, -1), new Vector3(0, -1, -1),
						new Vector3(-1, -1, 0)
									],
				CubeTopology.HalfSlopedCornerBase => [
						new Vector3(1, -1, 1), new Vector3(-1, -1, 1),
						new Vector3(-1, 0, 1), new Vector3(0, -0.5f, 1),
						new Vector3(0, -1, 1), new Vector3(1, 0, -1),
						new Vector3(1, -0.5f, 0), new Vector3(0, 0, 0),
						new Vector3(-1, 0, -1), new Vector3(-1, 0, 0),
						new Vector3(0, 0, -1), new Vector3(-1, -1, -1),
						new Vector3(0, -1, -1), new Vector3(1, -1, -1),
						new Vector3(1, -1, 0), new Vector3(-1, -1, 0)
									],
				CubeTopology.HalfCorner => [
						new Vector3(-1, 0, -1), new Vector3(1, 0, -1),
						new Vector3(-1, 0, 1), new Vector3(-1, 0, 0),
						new Vector3(0, 0, 0), new Vector3(0, 0, -1),
						new Vector3(1, -1, -1), new Vector3(-1, -1, 1),
						new Vector3(0, -1, 0), new Vector3(-1, -1, -1),
						new Vector3(0, -1, -1), new Vector3(-1, -1, 0)
									],
				CubeTopology.CornerSquare => [
						new Vector3(-1, -1, -1), new Vector3(0, -1, -1),
						new Vector3(1, -1, -1), new Vector3(-1, 0, -1),
						new Vector3(0, 0, -1), new Vector3(-1, 1, -1),
						new Vector3(-1, -1, 0), new Vector3(-1, -1, 1),
						new Vector3(-1, 0, 0), new Vector3(1, -1, 1),
						new Vector3(0, 0, 0), new Vector3(1, -1, 0),
						new Vector3(0, -1, 1)
									],
				CubeTopology.CornerSquareInverted => [
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
									],
				CubeTopology.SlopedCorner => [
						new Vector3(-1, 1, -1), new Vector3(1, 0, -1),
						new Vector3(-1, 0, 1), new Vector3(1, -1, 1),
						new Vector3(1, -0.5f, 0), new Vector3(0, 0.5f, -1),
						new Vector3(-1, 0.5f, 0), new Vector3(0, -0.5f, 1),
						new Vector3(-1, -1, 1), new Vector3(-1, -1, 0),
						new Vector3(-1, -1, -1), new Vector3(-1, 0, -1),
						new Vector3(1, -1, -1), new Vector3(1, -1, 0),
						new Vector3(0, -1, 1), new Vector3(0, -1, -1)
									],
				CubeTopology.SlopedCornerBase => [
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
									],
				CubeTopology.SlopedCornerTip => [
						new Vector3(1, -1, 1), new Vector3(-1, -1, 1),
						new Vector3(-1, 0, 1), new Vector3(0, -0.5f, 1),
						new Vector3(-1, -0.5f, 1), new Vector3(0, -1, 1),
						new Vector3(-1, -1, 0), new Vector3(-1, -1, -1),
						new Vector3(0, -1, 0), new Vector3(-1, -0.5f, 0)
									],
				CubeTopology.RaisedSlopedCorner => [
						new Vector3(1, 0, -1), new Vector3(-1, 0, 1),
						new Vector3(-1, 1, -1), new Vector3(-1, 0, -1),
						new Vector3(1, 0, -1), new Vector3(-1, 0, 1),
						new Vector3(-1, -1, 1), new Vector3(-1, -1, -1),
						new Vector3(-1, 0, -1), new Vector3(1, 0, 1),
						new Vector3(1, -1, 1), new Vector3(1, -1, -1)
									],
				CubeTopology.SlopeTransition => [
						new Vector3(1, -1, -1), new Vector3(1, -1, 0),
						new Vector3(1, -1, 1), new Vector3(1, -0.5f, 0),
						new Vector3(1, 0, -1), new Vector3(-1, -1, -1),
						new Vector3(-1, -1, 0), new Vector3(-1, -1, 1),
						new Vector3(-1, 0, -1), new Vector3(-1, 0, 0),
						new Vector3(-1, 1, -1), new Vector3(0, -1, -1),
						new Vector3(0, 0.5f, -1), new Vector3(0, -1, 1),
						new Vector3(0, 0, 0)
									],
				CubeTopology.SlopeTransitionBase => [
						new Vector3(-1, -1, 1), new Vector3(0, -1, 1),
						new Vector3(1, -1, 1), new Vector3(0, -0.5f, 1),
						new Vector3(-1, 0, 1), new Vector3(-1, 1, -1),
						new Vector3(0, 1, -1), new Vector3(-1, 0, -1),
						new Vector3(-1, -1, -1), new Vector3(0, -1, -1),
						new Vector3(1, 1, -1), new Vector3(1, 0, -1),
						new Vector3(1, -1, -1), new Vector3(1, 0, 0),
						new Vector3(0, 0.5f, 0), new Vector3(-1, -1, 0),
						new Vector3(1, -1, 0), new Vector3(-1, 0.5f, 0)
									],
				CubeTopology.SlopeTransitionBaseMirrored => [
						new Vector3(1, -1, -1), new Vector3(-1, 0, -1),
						new Vector3(1, 1, 1), new Vector3(0, -0.5f, -1),
						new Vector3(1, 0, 0), new Vector3(0, 0.5f, 0),
						new Vector3(-1, -1, -1), new Vector3(0, -1, -1),
						new Vector3(-1, -1, 1), new Vector3(-1, 0, 1),
						new Vector3(-1, -1, 0), new Vector3(-1, 1, 1),
						new Vector3(-1, 0.5f, 0), new Vector3(0, 1, 1),
						new Vector3(0, -1, 1), new Vector3(1, -1, 0),
						new Vector3(1, -1, 1), new Vector3(1, 0, 1)
									],
				CubeTopology.SlopeTransitionMirrored => [
						new Vector3(-1, -1, 1), new Vector3(-1, -1, 0),
						new Vector3(-1, -1, -1), new Vector3(-1, 0, 1),
						new Vector3(-1, 0, 0), new Vector3(-1, 1, 1),
						new Vector3(1, -1, 1), new Vector3(1, -1, 0),
						new Vector3(1, -1, -1), new Vector3(1, -0.5f, 0),
						new Vector3(1, 0, 1), new Vector3(0, -1, -1),
						new Vector3(0, 0, 0), new Vector3(0, 0.5f, 1),
						new Vector3(0, -1, 1)
									],
				CubeTopology.SlopeTransitionTip => [
						new Vector3(-1, 0, -1), new Vector3(0, 0, -1),
						new Vector3(-1, -1, -1), new Vector3(0, -1, -1),
						new Vector3(1, 0, -1), new Vector3(1, -1, -1),
						new Vector3(-1, -1, 1), new Vector3(1, -1, 0),
						new Vector3(0, -0.5f, 0), new Vector3(0, -1, 0.5f),
						new Vector3(1, -0.5f, -0.5f), new Vector3(-1, -1, 0),
						new Vector3(-1, -0.5f, 0)
									],
				CubeTopology.SlopeTransitionTipMirrored => [
						new Vector3(-1, -1, 1), new Vector3(-1, -1, 0),
						new Vector3(-1, -1, -1), new Vector3(-1, -0.5f, 0),
						new Vector3(-1, 0, 1), new Vector3(1, 0, 1),
						new Vector3(1, -1, 0), new Vector3(0, -0.5f, 0),
						new Vector3(0, -1, -0.5f), new Vector3(1, -0.5f, 0.5f),
						new Vector3(0, 0, 1), new Vector3(1, -1, 1),
						new Vector3(0, -1, 1)
									],
				CubeTopology.SquareSlopedCornerBase => [
						new Vector3(-1, -1, 1), new Vector3(-1, 0, 1),
						new Vector3(-1, -1, 0), new Vector3(-1, -1, -1),
						new Vector3(-1, 0, -1), new Vector3(-1, 0.5f, 0),
						new Vector3(-1, 1, -1), new Vector3(0, -1, -1),
						new Vector3(0, -1, 1), new Vector3(1, -1, -1),
						new Vector3(1, -1, 0), new Vector3(1, -1, 1),
						new Vector3(1, 0, -1), new Vector3(1, 0, 1),
						new Vector3(0, 0.5f, -1), new Vector3(0, 0.5f, 0),
						new Vector3(1, 0, 0), new Vector3(0, 0, 1)
									],
				CubeTopology.SquareSlopedCornerTip => [
						new Vector3(-1, -1, -1), new Vector3(1, -1, -1),
						new Vector3(-1, 0, -1), new Vector3(0, -0.5f, -1),
						new Vector3(0, -1, -1), new Vector3(-1, -0.5f, -1),
						new Vector3(-1, -1, 1), new Vector3(1, -1, 1),
						new Vector3(0, -1, 1), new Vector3(0, -0.5f, 0),
						new Vector3(-1, -0.5f, 0), new Vector3(1, -1, 0),
						new Vector3(-1, -1, 0)
									],
				CubeTopology.SquareSlopedCornerTipInv => [
						new Vector3(1, -1, -1), new Vector3(1, -1, 1),
						new Vector3(1, 0, -1), new Vector3(-1, 0, -1),
						new Vector3(-1, -1, -1), new Vector3(-1, -1, -1),
						new Vector3(-1, 0, -1), new Vector3(1, -1, 1),
						new Vector3(-1, 0, 1), new Vector3(-1, -1, 1),
						new Vector3(-1, -1, -1)
									],
				_ => [],
			};
		}
	}
}
