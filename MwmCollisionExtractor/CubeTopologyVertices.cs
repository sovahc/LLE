using VRageMath;

namespace MwmCollisionExtractor
{
	public enum CubeTopology
	{
		StandaloneBox, Box,
		Slope, RotatedSlope, RoundSlope,
		Corner, RotatedCorner, RoundCorner, InvCorner, RoundInvCorner, RoundedSlope, Slope2Base, Slope2Tip,
		Corner2Base, Corner2Tip, InvCorner2Base, InvCorner2Tip,
		HalfBox, HalfSlopeBox, HalfSlopeInverted, HalfSlopeCorner, HalfSlopeCornerInverted,
		SlopedCornerTip, SlopedCornerBase, SlopedCorner, HalfSlopedCornerBase, HalfCorner,
		CornerSquare, CornerSquareInverted, HalfSlopedCorner, RaisedSlopedCorner,
		SlopeTransition, SlopeTransitionBase, SlopeTransitionBaseMirrored, SlopeTransitionMirrored,
		SlopeTransitionTip, SlopeTransitionTipMirrored,
		SquareSlopedCornerBase, SquareSlopedCornerTip, SquareSlopedCornerTipInv
	}

	public static class CubeTopologyVertices
	{
		public static Vector3[] GetVertices(CubeTopology topology)
		{
			return topology switch
			{
				CubeTopology.StandaloneBox => [],
				CubeTopology.Box or CubeTopology.RoundedSlope => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  1, -1),
					new Vector3(-1,  1,  1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  1, -1),
					new Vector3( 1,  1,  1)
					],
				CubeTopology.Slope or CubeTopology.RotatedSlope => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  1, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  1, -1)
					],
				CubeTopology.RoundSlope => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1, 0.414f, 0.414f),
					new Vector3(-1,  1, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1, 0.414f, 0.414f),
					new Vector3( 1,  1, -1)
					],
				CubeTopology.Corner or CubeTopology.RotatedCorner => [
					new Vector3(-1, -1, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  1, -1)
					],
				CubeTopology.RoundCorner => [
					new Vector3(-1, -1, -1),
					new Vector3(-0.414f, -1, 0.414f),
					new Vector3(-0.414f, 0.414f, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1, 0.414f, 0.414f),
					new Vector3( 1,  1, -1)
					],
				CubeTopology.InvCorner => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  1, -1),
					new Vector3(-1,  1,  1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  1, -1),
					new Vector3( 1,  1,  1)
					],
				CubeTopology.RoundInvCorner => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  1, -1),
					new Vector3(-1,  1,  1),
					new Vector3(0.414f, -1, -0.414f),
					new Vector3(0.414f, -0.414f, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1, -0.414f, -0.414f),
					new Vector3( 1,  1, -1),
					new Vector3( 1,  1,  1)
					],
				CubeTopology.Slope2Base => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  0,  1),
					new Vector3(-1,  1, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  0,  1),
					new Vector3( 1,  1, -1)
					],
				CubeTopology.Slope2Tip => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  0, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  0, -1)
					],
				CubeTopology.Corner2Base => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  1, -1),
					new Vector3(0.5f, -0.5f, 0),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  0),
					new Vector3( 1,  0, -1)
					],
				CubeTopology.Corner2Tip => [
					new Vector3( 0, -1, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  0, -1)
					],
				CubeTopology.InvCorner2Base => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  1, -1),
					new Vector3(-1,  1,  1),
					new Vector3( 0, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  0, -1),
					new Vector3( 1,  1, -1),
					new Vector3( 1,  1,  1)
					],
				CubeTopology.InvCorner2Tip => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  1, -1),
					new Vector3(-1,  1,  1),
					new Vector3( 0, -1,  0),
					new Vector3( 0, -1,  1),
					new Vector3( 1,  0,  0),
					new Vector3( 1,  0,  1),
					new Vector3( 1,  1, -1),
					new Vector3( 1,  1,  1)
					],
				CubeTopology.HalfBox => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  0),
					new Vector3(-1,  1, -1),
					new Vector3(-1,  1,  0),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  0),
					new Vector3( 1,  1, -1),
					new Vector3( 1,  1,  0)
					],
				CubeTopology.HalfSlopeBox => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  0),
					new Vector3(-1,  0, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  0),
					new Vector3( 1,  0, -1)
					],
				CubeTopology.HalfSlopeInverted => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  1, -1),
					new Vector3(-1,  1,  1),
					new Vector3( 0,  1, -1),
					new Vector3( 0,  1,  1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  0, -1),
					new Vector3( 1,  0,  1)
					],
				CubeTopology.HalfSlopeCorner => [
					new Vector3( 0, -1,  1),
					new Vector3( 1, -1,  0),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  0,  1)
					],
				CubeTopology.HalfSlopeCornerInverted => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  0, -1),
					new Vector3(-1,  1,  0),
					new Vector3(-1,  1,  1),
					new Vector3( 0,  1, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  1, -1),
					new Vector3( 1,  1,  1)
					],
				CubeTopology.HalfSlopedCorner => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  0,  1),
					new Vector3(-1,  1, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1,  0, -1)
					],
				CubeTopology.HalfSlopedCornerBase => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  0, -1),
					new Vector3(-1,  0,  1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  0, -1)
					],
				CubeTopology.HalfCorner => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  0, -1),
					new Vector3(-1,  0,  1),
					new Vector3( 1, -1, -1),
					new Vector3( 1,  0, -1)
					],
				CubeTopology.CornerSquare => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  1, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1)
					],
				CubeTopology.CornerSquareInverted => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  1, -1),
					new Vector3(-1,  1,  1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  1, -1)
					],
				CubeTopology.SlopedCorner => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  0,  1),
					new Vector3(-1,  1, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  0, -1)
					],
				CubeTopology.SlopedCornerBase => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  0,  1),
					new Vector3(-1,  1, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  1, -1),
					new Vector3( 1,  1,  1)
					],
				CubeTopology.SlopedCornerTip => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  0,  1),
					new Vector3( 1, -1,  1)
					],
				CubeTopology.RaisedSlopedCorner => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  0,  1),
					new Vector3(-1,  1, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  0, -1),
					new Vector3( 1,  0,  1)
					],
				CubeTopology.SlopeTransition => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  1, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  0, -1)
					],
				CubeTopology.SlopeTransitionBase => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  0,  1),
					new Vector3(-1,  1, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  1, -1)
					],
				CubeTopology.SlopeTransitionBaseMirrored => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  0, -1),
					new Vector3(-1,  1,  1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  1,  1)
					],
				CubeTopology.SlopeTransitionMirrored => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  1,  1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  0,  1)
					],
				CubeTopology.SlopeTransitionTip => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  0, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  0),
					new Vector3( 1,  0, -1)
					],
				CubeTopology.SlopeTransitionTipMirrored => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  0,  1),
					new Vector3( 1, -1,  0),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  0,  1)
					],
				CubeTopology.SquareSlopedCornerBase => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  0,  1),
					new Vector3(-1,  1, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  0, -1),
					new Vector3( 1,  0,  1)
					],
				CubeTopology.SquareSlopedCornerTip => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  0, -1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1)
					],
				CubeTopology.SquareSlopedCornerTipInv => [
					new Vector3(-1, -1, -1),
					new Vector3(-1, -1,  1),
					new Vector3(-1,  0, -1),
					new Vector3(-1,  0,  1),
					new Vector3( 1, -1, -1),
					new Vector3( 1, -1,  1),
					new Vector3( 1,  0, -1)
					],
				_ => [],
			};
		}
	}
}
