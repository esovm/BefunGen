﻿using BefunGen.AST.Exceptions;
using BefunGen.MathExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BefunGen.AST.CodeGen
{
	public class CodePiece
	{
		#region Properties

		public int MinX { get; private set; } // Minimal ::> Inclusive

		public int MinY { get; private set; }

		public int MaxX { get; private set; } // Maximal + 1 ::> Exclusive

		public int MaxY { get; private set; }

		public int Width { get { return MaxX - MinX; } }

		public int Height { get { return MaxY - MinY; } }

		private List<List<BefungeCommand>> commandArr = new List<List<BefungeCommand>>();

		public BefungeCommand this[int x, int y] { get { return get(x, y); } set { set(x, y, value); } }

		#endregion

		#region Konstruktor
		public CodePiece()
		{
			MinX = 0;
			MinY = 0;

			MaxX = 0;
			MaxY = 0;
		}
		#endregion

		#region Internal

		private bool IsIncluded(int x, int y)
		{
			return x >= MinX && y >= MinY && x < MaxX && y < MaxY;
		}

		private bool expand(int x, int y)
		{
			bool ex = expandX(x);
			bool ey = expandY(y);

			return ex && ey;
		}

		private bool expandX(int x)
		{
			if (x >= MaxX) // expand Right
			{
				int newMaxX = x + 1;

				while (MaxX < newMaxX)
				{
					commandArr.Add(Enumerable.Repeat(BCHelper.Unused, Height).ToList());

					MaxX++;
				}

				return true;
			}
			else if (x < MinX)
			{
				int newMinX = x;

				while (MinX > newMinX)
				{
					commandArr.Insert(0, Enumerable.Repeat(BCHelper.Unused, Height).ToList());

					MinX--;
				}
			}

			return false;
		}

		private bool expandY(int y)
		{
			if (y >= MaxY) // expand Right
			{
				int newMaxY = y + 1;

				while (MaxY < newMaxY)
				{
					for (int xw = 0; xw < Width; xw++)
					{
						commandArr[xw].Add(BCHelper.Unused);
					}

					MaxY++;
				}

				return true;
			}
			else if (y < MinY)
			{
				int newMinY = y;

				while (MinY > newMinY)
				{
					for (int xw = 0; xw < Width; xw++)
					{
						commandArr[xw].Insert(0, BCHelper.Unused);
					}

					MinY--;
				}
			}

			return false;
		}

		private void set(int x, int y, BefungeCommand value)
		{
			if (!IsIncluded(x, y))
				expand(x, y);

			if (commandArr[x - MinX][y - MinY].Type != BefungeCommandType.NOP)
				throw new InvalidCodeManipulationException("Modification of CodePiece : " + x + "|" + y);

			if (hasTag(value.Tag))
				throw new InvalidCodeManipulationException(string.Format("Duplicate Tag in CodePiece : [{0},{1}] = '{2}' = [{3},{4}])",x, y, value.Tag.ToString(), findTag(value.Tag).Item2, findTag(value.Tag).Item3));

			commandArr[x - MinX][y - MinY] = value;
		}

		private BefungeCommand get(int x, int y)
		{
			if (IsIncluded(x, y))
				return commandArr[x - MinX][y - MinY];
			else 
				return BCHelper.Unused;
		}

		public override string ToString()
		{
			StringBuilder builder = new StringBuilder();
			builder.AppendLine(string.Format("{0}: [{1} - {2}, {3} - {4}] ({5}, {6})", this.GetType().Name, MinX, MaxX, MinY, MaxY, Width, Height));

			builder.AppendLine("{");
			for (int y = MinY; y < MaxY; y++)
			{
				for (int x = MinX; x < MaxX; x++)
				{
					BefungeCommand bc = this[x, y];
					builder.Append(bc.getCommandCode());
				}
				builder.AppendLine();
			}
			builder.AppendLine("}");

			return builder.ToString();
		}

		#endregion

		#region Tags

		public Tuple<BefungeCommand, int, int> findTag(object tag)
		{
			for (int x = MinX; x < MaxX; x++)
			{
				for (int y = MinY; y < MaxY; y++)
				{
					if (this[x, y].Tag == tag)
						return Tuple.Create(this[x, y], x, y);
				}
			}

			return null;
		}

		public bool hasTag(object tag)
		{
			return tag != null && findTag(tag) != null;
		}

		#endregion

		#region Normalize

		public void normalize()
		{
			normalizeX();
			normalizeY();
		}

		public void normalizeY()
		{
			int oy = -MinY;
			MinY += oy;
			MaxY += oy;
		}

		public void normalizeX()
		{
			int ox = -MinX;
			MinX += ox;
			MaxX += ox;
		}

		#endregion

		#region Copy

		public CodePiece copy()
		{
			CodePiece result = new CodePiece();
			for (int x = 0; x < commandArr.Count; x++)
			{
				for (int y = 0; y < commandArr[x].Count; y++)
				{
					result[x, y] = commandArr[x][y];
				}
			}

			result.MinX = MinX;
			result.MinY = MinY;

			result.MaxX = MaxX;
			result.MaxY = MaxY;

			return result;
		}

		public CodePiece copyNormalized()
		{
			CodePiece result = copy();
			result.normalize();
			return result;
		}

		#endregion

		#region Combine

		public static CodePiece CombineHorizontal(CodePiece left, CodePiece right)
		{
			CodePiece c_l = left.copy();
			CodePiece c_r = right.copy();

			c_l.normalizeX();
			c_r.normalizeX();

			c_l.AppendRight(c_r);

			return c_l;
		}

		public static CodePiece CombineVertical(CodePiece top, CodePiece bottom)
		{
			CodePiece c_t = top.copy();
			CodePiece c_b = bottom.copy();

			c_t.normalizeY();
			c_b.normalizeY();

			c_t.AppendBottom(c_b);

			return c_t;
		}

		#endregion

		#region Setter

		// x1, y1 included -- x2, y2 excluded
		public void Fill(int x1, int y1, int x2, int y2, BefungeCommand c, object topleft_tag = null)
		{
			if (x1 > x2) MathExt.Swap(ref x1, ref x2);
			if (y1 > y2) MathExt.Swap(ref y1, ref y2);

			for (int x = x1; x < x2; x++)
				for (int y = y1; y < y2; y++)
				{
					if (x == x1 && y == y1 && topleft_tag != null)
					{
						this[x, y] = c.copyWithTag(topleft_tag);
					}
					else
					{
						this[x, y] = c;
					}
				}

		}

		public void SetAt(int paramX, int paramY, CodePiece lit)
		{
			for (int x = lit.MinX; x < lit.MaxX; x++)
			{
				for (int y = lit.MinY; y < lit.MaxY; y++)
				{
					this[x + paramX, y + paramY] = lit[x, y];
				}
			}
		}

		#endregion

		#region Append

		public void AppendRight(BefungeCommand c)
		{
			AppendRight(0, c);
		}

		public void AppendRight(int row, BefungeCommand c)
		{
			CodePiece p = new CodePiece();
			p[0, row] = c;

			AppendRight(p);
		}

		public void AppendRight(CodePiece right)
		{
			right = right.copy();

			CodePiece compress_conn;
			if (CodeGenOptions.CompressHorizontalCombining && (compress_conn = doCompressHorizontally(this, right)) != null)
			{
				this.RemoveColumn(this.MaxX - 1);
				right.RemoveColumn(right.MinX);

				this.AppendRightDirect(compress_conn);
			}

			AppendRightDirect(right);
		}

		private void AppendRightDirect(CodePiece right)
		{
			right.normalizeX();

			int offset = MaxX;

			for (int x = right.MinX; x < right.MaxX; x++)
			{
				for (int y = right.MinY; y < right.MaxY; y++)
				{
					this[offset + x, y] = right[x, y];
				}
			}
		}

		public void AppendLeft(BefungeCommand c)
		{
			AppendLeft(0, c);
		}

		public void AppendLeft(int row, BefungeCommand c)
		{
			CodePiece p = new CodePiece();
			p[0, row] = c;

			AppendLeft(p);
		}

		public void AppendLeft(CodePiece left)
		{
			left = left.copy();

			CodePiece compress_conn;
			if (CodeGenOptions.CompressHorizontalCombining && (compress_conn = doCompressHorizontally(left, this)) != null)
			{
				this.RemoveColumn(this.MinX);
				left.RemoveColumn(left.MaxX - 1);

				this.AppendLeftDirect(compress_conn);
			}

			AppendLeftDirect(left);
		}

		private void AppendLeftDirect(CodePiece left)
		{
			left.normalizeX();

			int offset = MinX - left.MaxX;

			for (int x = left.MinX; x < left.MaxX; x++)
			{
				for (int y = left.MinY; y < left.MaxY; y++)
				{
					this[offset + x, y] = left[x, y];
				}
			}
		}

		public void AppendBottom(BefungeCommand c)
		{
			AppendBottom(0, c);
		}

		public void AppendBottom(int col, BefungeCommand c)
		{
			CodePiece p = new CodePiece();
			p[col, 0] = c;

			AppendBottom(p);
		}

		public void AppendBottom(CodePiece bot) //TODO Compress
		{
			bot.normalizeY();

			int offset = MaxY;

			for (int x = bot.MinX; x < bot.MaxX; x++)
			{
				for (int y = bot.MinY; y < bot.MaxY; y++)
				{
					this[x, offset + y] = bot[x, y];
				}
			}
		}

		public void AppendTop(BefungeCommand c)
		{
			AppendTop(0, c);
		}

		public void AppendTop(int col, BefungeCommand c)
		{
			CodePiece p = new CodePiece();
			p[col, 0] = c;

			AppendTop(p);
		}

		public void AppendTop(CodePiece top)
		{
			top.normalizeY();

			int offset = MinY - top.MaxY;

			for (int x = top.MinX; x < top.MaxX; x++)
			{
				for (int y = top.MinY; y < top.MaxY; y++)
				{
					this[x, offset + y] = top[x, y];
				}
			}
		}

		#endregion

		#region Characteristics

		public bool IsHFlat() // Is Horizontal Flat
		{
			return Height == 1;
		}

		public bool IsVFlat() // Is Vertical Flat
		{
			return Width == 1;
		}

		public bool lastRowIsSingle()
		{
			return IsRowSingle(Width - 1);
		}

		public bool firstRowIsSingle()
		{
			return IsRowSingle(0);
		}

		public bool IsRowSingle(int r)
		{
			return commandArr[r].Count(p => p.Type != BefungeCommandType.NOP) == 1;
		}

		#endregion

		#region Optimizing

		public static CodePiece doCompressHorizontally(CodePiece l, CodePiece r)
		{
			if (l.Width == 0 || r.Width == 0)
				return null;

			CodePiece connect = new CodePiece();

			int x_l = l.MaxX - 1;
			int x_r = r.MinX;

			for (int y = Math.Min(l.MinY, r.MinY); y < Math.Max(l.MaxY, r.MaxY); y++)
			{
				object Tag = null;

				if (l[x_l, y].Tag != null && r[x_r, y].Tag != null)
				{
					return null; // Can't compress - two tags would need to be merged
				}

				Tag = l[x_l, y].Tag ?? r[x_r, y].Tag;

				if (l[x_l, y].Type == BefungeCommandType.NOP && r[x_r, y].Type == BefungeCommandType.NOP)
				{
					connect[0, y] = new BefungeCommand(BefungeCommandType.NOP, Tag);
				}
				else if (l[x_l, y].Type != BefungeCommandType.NOP && r[x_r, y].Type != BefungeCommandType.NOP) 
				{
					return null; // Can't compress - two commands are colliding
					// Wouldn't even work when they are the same (eg stringmode_toogle ord stack-manipulation can't be merged)
				}
				else if (l[x_l, y].Type != BefungeCommandType.NOP)
				{
					connect[0, y] = new BefungeCommand(l[x_l, y].Type, l[x_l, y].Param, Tag);
				}
				else if (r[x_r, y].Type != BefungeCommandType.NOP)
				{
					connect[0, y] = new BefungeCommand(r[x_r, y].Type, r[x_r, y].Param, Tag);
				}
				else
				{
					throw new WTFException();
				}
			}

			return connect;
		}

		#endregion

		#region Modify

		public void RemoveColumn(int col)
		{
			int abs = col - MinX;

			commandArr.RemoveAt(abs);

			MaxX = MaxX - 1;
		}

		public void RemoveRow(int row)
		{
			int abs = row - MinY;

			for (int i = 0; i < Width; i++)
			{
				commandArr[i].RemoveAt(abs);
			}

			MaxY = MaxY - 1;
		}

		public void reverseX()
		{
			CodePiece p = this.copy();

			this.Clear();

			for (int x = p.MinX; x < p.MaxX; x++)
			{
				for (int y = p.MinY; y < p.MaxY; y++)
				{
					if (!p[x, y].IsXDeltaIndependent())
						throw new CodePieceReverseException(p);
					
					this[-x, y] = p[x, y];
				}
			}

			this.normalizeX();
		}

		public void Clear()
		{
			commandArr.Clear();

			MinX = 0;
			MinY = 0;

			MaxX = 0;
			MaxY = 0;
		}

		#endregion
	}
}