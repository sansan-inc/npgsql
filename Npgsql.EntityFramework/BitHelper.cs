using System;
using System.Collections.Generic;
using System.Data.Entity.Core.Common.CommandTrees;
using System.Data.Entity.Core.Common.CommandTrees.ExpressionBuilder;
using System.Data.Entity.Core.Metadata.Edm;
using System.Linq;
using System.Text;

namespace Npgsql
{
    internal static class BitHelper
    {
        public static bool IsBit(this TypeUsage typeUsage)
        {
            var primitiveType = typeUsage.EdmType as PrimitiveType;
            if (primitiveType == null || primitiveType.PrimitiveTypeKind != PrimitiveTypeKind.Int32)
                return false;
            return typeUsage.Facets.Contains("Precision");
        }

        public static byte GetBitLength(this TypeUsage typeUsage)
        {
            return (byte) typeUsage.Facets["Precision"].Value;
        }

        public static string GetBitStoreTypeString(this TypeUsage typeUsage)
        {
            var bitLength = typeUsage.GetBitLength();
            if (bitLength > 0)
                return "bit(" + bitLength + ")";
            return "bit";
        }

        public static Tuple<DbExpression, DbExpression> AdjustBitwiseArguments(DbExpression arg0, DbExpression arg1)
        {
            // * "Extent1"."Flags" & 1
            //    -> CAST("Extent1"."Flags" AS int4) & 1
            var arg0IsBit = arg0.ResultType.IsBit();
            if (arg0IsBit ^ arg1.ResultType.IsBit())
            {
                if (arg0IsBit)
                    arg0 = arg0.CastTo(arg1.ResultType);
                else
                    arg1 = arg1.CastTo(arg0.ResultType);
            }
            return Tuple.Create(arg0, arg1);
        }

        public static Tuple<DbExpression, DbExpression> AdjustBitComparison(DbComparisonExpression expression)
        {
            var left = expression.Left;
            var right = expression.Right;

            // * 1 = CAST ("Extent1"."Flags" AS int4)
            //    -> 1 = "Extent1"."Flags"
            // * CAST ("Extent1"."Flags" AS int4) = CAST ("Extent1"."Flags" AS int4)
            //    -> "Extent1"."Flags" = "Extent1"."Flags"
            var leftCast = left as DbCastExpression;
            var rightCast = right as DbCastExpression;
            if (leftCast != null && leftCast.Argument.ResultType.IsBit())
            {
                if (rightCast != null && rightCast.Argument.ResultType.IsBit())
                {
                    left = leftCast.Argument;
                    right = rightCast.Argument;
                }
                else
                {
                    left = leftCast.Argument;
                }
            }
            else if (rightCast != null && rightCast.Argument.ResultType.IsBit())
            {
                right = rightCast.Argument;
            }

            // 1 = "Extent1"."Flags"
            // -> CAST (1 AS bit(8)) = "Extent1"."Flags"
            var leftIsBit = left.ResultType.IsBit();
            if (leftIsBit ^ right.ResultType.IsBit())
            {
                if (leftIsBit)
                    right = right.CastTo(left.ResultType);
                else
                    left = left.CastTo(right.ResultType);
            }

            return Tuple.Create(left, right);
        }

        public static DbCaseExpression AdjustBitContainedCase(DbCaseExpression expression)
        {
            var bitResultType = expression.Then.Select(x => x.ResultType).FirstOrDefault(x => x.IsBit());
            if (bitResultType == null)
            {
                if (!expression.Else.ResultType.IsBit())
                    return expression;

                bitResultType = expression.Else.ResultType;
            }

            return DbExpressionBuilder.Case(
                expression.When,
                expression.Then.Select(x => x.CastToBitIfNotBit(bitResultType)),
                expression.Else is DbNullExpression ? expression.Else : expression.Else.CastToBitIfNotBit(bitResultType));
        }

        private static DbExpression CastToBitIfNotBit(this DbExpression expression, TypeUsage bitType)
        {
            if (expression.ResultType.IsBit())
                return expression;
            return expression.CastTo(bitType);
        }
    }
}
