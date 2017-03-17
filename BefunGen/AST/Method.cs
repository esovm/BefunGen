using BefunGen.AST.CodeGen;
using BefunGen.AST.CodeGen.Tags;
using BefunGen.AST.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BefunGen.AST
{
	public class Method : ASTObject
	{
		private static int _methodaddressCounter = 0;
		protected static int METHODADDRESS_COUNTER { get { return _methodaddressCounter++; } }

		public Program Owner;

		public readonly BType ResultType;
		public readonly string Identifier;
		public readonly List<VarDeclaration> Parameter;

		public readonly List<VarDeclaration> Variables; // Includes Parameter & Temps
		public readonly StatementStatementList Body;

		public int ReferenceCount { get { return References.Count; } }
		public List<StatementMethodCall> References = new List<StatementMethodCall>(); // Can Contain Reference <null> (in MainMethod)

		private int methodaddress = -1;
		public int MethodAddr { get { return methodaddress; } private set { methodaddress = value; } }

		public Method(SourceCodePosition pos, MethodHeader h, MethodBody b)
			: this(h.Position, h.ResultType, h.Identifier, h.Parameter, b.Variables, b.Body)
		{
			//--
		}

		public Method(SourceCodePosition pos, BType t, string id, List<VarDeclaration> p, List<VarDeclaration> v, StatementStatementList b)
			: base(pos)
		{
			this.ResultType = t;
			this.Identifier = id;
			this.Parameter = p;

			this.Variables = v;
			this.Body = b;

			Variables.AddRange(Parameter);
		}

		public override string GetDebugString()
		{
			return string.Format("#Method ({{{0}}}({1}):{2})\n[\n#References: {3}\n#Parameter:\n{4}\n#Variables:\n{5}\n#Body:\n{6}\n]",
				MethodAddr,
				Identifier,
				ResultType.GetDebugString(),
				References.Count,
				Indent(GetDebugStringForList(Parameter)),
				Indent(GetDebugStringForList(Variables.Where(p => !Parameter.Contains(p)).ToList())),
				Indent(Body.GetDebugString()));
		}

		public string GetWellFormattedHeader()
		{
			return string.Format("{0} {1}({2});", ResultType, Identifier, string.Join(", ", Parameter.Select(p => p.GetWellFormattedDecalaration())));
		}

		public void CreateCodeAddress()
		{
			MethodAddr = METHODADDRESS_COUNTER;
		}

		public void IntegrateStatementLists()
		{
			Body.IntegrateStatementLists();
		}

		public void LinkVariables()
		{
			Body.LinkVariables(this);
		}

		public void InlineConstants()
		{
			Body.InlineConstants();
		}

		public void AddressCodePoints()
		{
			Body.AddressCodePoints();
		}

		public void LinkMethods(Program owner)
		{
			Body.LinkMethods(owner);
		}

		public void LinkResultTypes()
		{
			Body.LinkResultTypes(this);
		}

		public void ForceMethodReturn(bool isMain)
		{
			if (!Body.AllPathsReturn())
			{
				if (isMain)
				{
					Body.List.Add(new StatementQuit(Position));
				}
				else if (ResultType is BTypeVoid)
				{
					Body.List.Add(new StatementReturn(Position));
				}
				else
				{
					throw new NotAllPathsReturnException(this, Position);
				}
			}
		}

		public void RaiseErrorOnReturnStatement()
		{
			StatementReturn sr;
			if ((sr = Body.HasReturnStatement()) != null)
			{
				throw new IllegalReturnCallInMainException(sr.Position);
			}
		}

		public void EvaluateExpressions()
		{
			Body.EvaluateExpressions();
		}

		public void AddReference(StatementMethodCall rf)
		{
			References.Add(rf);
		}

		public VarDeclaration FindVariableByIdentifier(string ident)
		{
			List<VarDeclaration> r = Variables.Where(p => p.Identifier.ToLower() == ident.ToLower())
				.Concat(Owner.Variables.Where(p => p.Identifier.ToLower() == ident.ToLower()))
				.Concat(Owner.Constants.Where(p => p.Identifier.ToLower() == ident.ToLower()))
				.ToList();

			return r.Count() == 1 ? r.Single() : null;
		}

		public StatementLabel FindLabelByIdentifier(string ident)
		{
			return Body.FindLabelByIdentifier(ident);
		}

		public static void ResetCounter()
		{
			_methodaddressCounter = 0;
		}

		#region GenerateCode

		public CodePiece GenerateCode(int methOffsetX, int methOffsetY)
		{
			CodePiece p = new CodePiece();

			// Generate Space for Variables
			p.AppendBottom(GenerateCode_Variables(methOffsetX, methOffsetY));

			// Generate Initialization of Variables
			CodePiece pVi = GenerateCode_VariableIntialization();
			pVi.SetTag(0, 0, new MethodEntryFullInitializationTag(this));  //v<-- Entry Point
			p.AppendBottom(pVi);

			// Generate Initialization of Parameters
			p.AppendBottom(GenerateCode_ParameterIntialization());

			// Generate Statements
			p.AppendBottom(GenerateCode_Body());

			return p;
		}

		private CodePiece GenerateCode_Variables(int moX, int moY)
		{
			CodePiece p = new CodePiece();

			int paramX = 0;
			int paramY = 0;

			int maxArr = 0;
			if (Variables.Count(t => t is VarDeclarationArray) > 0)
				maxArr = Variables.Where(t => t is VarDeclarationArray).Select(t => t as VarDeclarationArray).Max(t => t.Size);

			int maxwidth = Math.Max(maxArr, CGO.DefaultVarDeclarationWidth);

			for (int i = 0; i < Variables.Count; i++)
			{
				VarDeclaration var = Variables[i];

				CodePiece lit = new CodePiece();

				if (paramX >= maxwidth)
				{	// Next Line
					paramX = 0;
					paramY++;
				}

				if (paramX > 0 && var is VarDeclarationArray && (paramX + (var as VarDeclarationArray).Size) > maxwidth)
				{	// Next Line
					paramX = 0;
					paramY++;
				}

				if (var is VarDeclarationValue)
				{
					lit[0, 0] = BCHelper.Chr(CGO.DefaultVarDeclarationSymbol,new VarDeclarationTag(var));
				}
				else
				{
					int sz = (var as VarDeclarationArray).Size;
					lit.Fill(0, 0, sz, 1, BCHelper.Chr(CGO.DefaultVarDeclarationSymbol), new VarDeclarationTag(var));
				}

				var.CodePositionX = moX + paramX;
				var.CodePositionY = moY + paramY;

				p.SetAt(paramX, paramY, lit);
				paramX += lit.Width;
			}

			return p;
		}

		private CodePiece GenerateCode_VariableIntialization()
		{
			CodePiece p = new CodePiece();

			List<TwoDirectionCodePiece> varDecls = new List<TwoDirectionCodePiece>();

			for (int i = 0; i < Variables.Count; i++)
			{
				VarDeclaration var = Variables[i];

				if (Parameter.Contains(var))
					continue;

				varDecls.Add(new TwoDirectionCodePiece(var.GenerateCode(false), var.GenerateCode(true)));
			}

			if (varDecls.Count % 2 != 0)
				varDecls.Add(new TwoDirectionCodePiece());

			varDecls = varDecls.OrderByDescending(t => t.MaxWidth).ToList();


			for (int i = 0; i < varDecls.Count; i += 2)
			{
				CodePiece cpA = varDecls[i].Normal;
				CodePiece cpB = varDecls[i + 1].Reversed;

				cpA.NormalizeX();
				cpB.NormalizeX();

				int mw = Math.Max(cpA.MaxX, cpB.MaxX);

				cpA.AppendLeft(BCHelper.PCRight);
				cpB.AppendLeft(BCHelper.PCDown);

				cpA.Fill(cpA.MaxX, 0, mw, 1, BCHelper.Walkway);
				cpB.Fill(cpB.MaxX, 0, mw, 1, BCHelper.Walkway);

				cpA[mw, 0] = BCHelper.PCDown;
				cpB[mw, 0] = BCHelper.PCLeft;


				cpA.FillColWw(cpA.MaxX - 1, 1, cpA.MaxY);
				cpA.FillColWw(cpA.MinX, cpA.MinY, 0);

				cpB.FillColWw(cpB.MaxX - 1, cpB.MinY, 0);
				cpB.FillColWw(cpB.MinX, 1, cpB.MaxY);


				cpA.NormalizeX();
				cpB.NormalizeX();

				p.AppendBottom(cpA);
				p.AppendBottom(cpB);
			}

			p.NormalizeX();

			p.ForceNonEmpty(BCHelper.PCDown);

			return p;
		}

		private CodePiece GenerateCode_ParameterIntialization()
		{
			CodePiece p = new CodePiece();

			List<TwoDirectionCodePiece> paramDecls = new List<TwoDirectionCodePiece>();

			for (int i = Parameter.Count - 1; i >= 0; i--)
			{
				VarDeclaration var = Parameter[i];

				paramDecls.Add(new TwoDirectionCodePiece(var.GenerateCode_SetToStackVal(false), var.GenerateCode_SetToStackVal(true)));
			}

			if (paramDecls.Count % 2 != 0)
				paramDecls.Add(new TwoDirectionCodePiece());

			for (int i = 0; i < paramDecls.Count; i += 2)
			{
				CodePiece cpA = paramDecls[i].Normal;
				CodePiece cpB = paramDecls[i + 1].Reversed;

				cpA.NormalizeX();
				cpB.NormalizeX();

				int mw = Math.Max(cpA.MaxX, cpB.MaxX);

				cpA[-1, 0] = BCHelper.PCRight;
				cpB[-1, 0] = BCHelper.PCDown;

				cpA.Fill(cpA.MaxX, 0, mw, 1, BCHelper.Walkway);
				cpB.Fill(cpB.MaxX, 0, mw, 1, BCHelper.Walkway);

				cpA[mw, 0] = BCHelper.PCDown;
				cpB[mw, 0] = BCHelper.PCLeft;

				for (int y = cpA.MinY; y < cpA.MaxY; y++)
					if (y != 0)
						cpA[mw, y] = BCHelper.Walkway;

				for (int y = cpB.MinY; y < cpB.MaxY; y++)
					if (y != 0)
						cpB[-1, y] = BCHelper.Walkway;

				cpA.NormalizeX();
				cpB.NormalizeX();

				p.AppendBottom(cpA);
				p.AppendBottom(cpB);
			}

			p.NormalizeX();

			return p;
		}

		private CodePiece GenerateCode_Body()
		{
			CodePiece p = Body.GenerateStrippedCode();

			p.NormalizeX();

			p[-1, 0] = BCHelper.PCRight;

			p.NormalizeX();

			p.Fill(0, p.MinY, 1, 0, BCHelper.Walkway);

			return p;
		}

		#endregion
	}

	public class MethodHeader : ASTObject // TEMPORARY -- NOT IN RESULTING AST
	{
		public readonly BType ResultType;
		public readonly string Identifier;
		public readonly List<VarDeclaration> Parameter;

		public MethodHeader(SourceCodePosition pos, BType t, string ident, List<VarDeclaration> p)
			: base(pos)
		{
			this.ResultType = t;
			this.Identifier = ident;
			this.Parameter = p;

			if (ASTObject.IsKeyword(ident))
			{
				throw new IllegalIdentifierException(Position, ident);
			}
		}

		public override string GetDebugString()
		{
			throw new InvalidAstStateException(Position);
		}
	}

	public class MethodBody : ASTObject // TEMPORARY -- NOT IN RESULTING AST
	{
		public readonly List<VarDeclaration> Variables;
		public readonly StatementStatementList Body;

		public MethodBody(SourceCodePosition pos, List<VarDeclaration> v, StatementStatementList b)
			: base(pos)
		{
			this.Variables = v;
			this.Body = b;

			if (Variables.Any(lp1 => Variables.Any(lp2 => lp1.Identifier.ToLower() == lp2.Identifier.ToLower() && lp1 != lp2)))
			{
				VarDeclaration err = Variables.Last(lp1 => Variables.Any(lp2 => lp1.Identifier.ToLower() == lp2.Identifier.ToLower()));
				throw new DuplicateIdentifierException(err.Position, err.Identifier);
			}
		}

		public override string GetDebugString()
		{
			throw new InvalidAstStateException(Position);
		}
	}
}