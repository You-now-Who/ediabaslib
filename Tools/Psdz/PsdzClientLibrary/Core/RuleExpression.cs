﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PsdzClient.Core
{
	[Serializable]
	public abstract class RuleExpression : IRuleExpression
	{
        public class FormulaConfig
        {
            public FormulaConfig(string getStringFunc, string getLongFunc, string checkStringFunc, string checkLongFunc, string operatorSeparator = null)
            {
                GetStringFunc = getStringFunc;
                GetLongFunc = getLongFunc;
                CheckStringFunc = checkStringFunc;
                CheckLongFunc = checkLongFunc;
                OperatorSeparator = operatorSeparator;
            }

            public string GetStringFunc { get; private set; }
            public string GetLongFunc { get; private set; }
            public string CheckStringFunc { get; private set; }
            public string CheckLongFunc { get; private set; }
            public string OperatorSeparator { get; private set; }
        }

        // ToDo: Check on update
        public enum EExpressionType
        {
            COMP = 0,
            AND = 1,
            OR = 2,
            NOT = 3,
            DATE = 4,
            VALUE = 5,
            ISTUFE = 6,
            VALID_FROM = 7,
            VALID_TO = 8,
            COUNTRY = 9,
            ECUGROUP = 10,
            ECUVARIANT = 11,
            ECUCLIQUE = 12,
            EQUIPMENT = 13,
            SALAPA = 14,
            SIFA = 15,
            VARIABLE = 16,
            CHARACTERISTIC = 17,
            ECUREPRESENTATIVE = 18,
            MANUFACTORINGDATE = 19,
            ISTUFEX = 20,
            ECUPROGRAMMINGVARIANT = 22
        }

        public enum EEvaluationResult
        {
            VALID,
            INVALID,
            MISSING_CHARACTERISTIC,
            MISSING_VARIANT
        }

        public enum ESymbolType
        {
            Unknown,
            Value,
            Operator,
            TerminalAnd,
            TerminalOr,
            TerminalNot,
            TerminalLPar,
            TerminalRPar,
            TerminalProduktionsdatum,
            DateExpression,
            CompareExpression,
            NotExpression,
            OrExpression,
            AndExpression,
            Expression,
            VariableExpression
        }

        // ToDo: Check on update
        public static RuleExpression Deserialize(Stream ms, Vehicle vec)
        {
            EExpressionType eExpressionType = (EExpressionType)(byte)ms.ReadByte();
            switch (eExpressionType)
            {
                case EExpressionType.COMP:
                    return CompareExpression.Deserialize(ms, vec);
                case EExpressionType.AND:
                    return AndExpression.Deserialize(ms, vec);
                case EExpressionType.OR:
                    return OrExpression.Deserialize(ms, vec);
                case EExpressionType.NOT:
                    return NotExpression.Deserialize(ms, vec);
                case EExpressionType.DATE:
                    return DateExpression.Deserialize(ms, vec);
                case EExpressionType.CHARACTERISTIC:
                    return CharacteristicExpression.Deserialize(ms, vec);
                case EExpressionType.MANUFACTORINGDATE:
                    return ManufactoringDateExpression.Deserialize(ms, vec);
                case EExpressionType.ISTUFEX:
                    return IStufeXExpression.Deserialize(ms, vec);
                case EExpressionType.ISTUFE:
                case EExpressionType.VALID_FROM:
                case EExpressionType.VALID_TO:
                case EExpressionType.COUNTRY:
                case EExpressionType.ECUGROUP:
                case EExpressionType.ECUVARIANT:
                case EExpressionType.ECUCLIQUE:
                case EExpressionType.EQUIPMENT:
                case EExpressionType.SALAPA:
                case EExpressionType.SIFA:
                case EExpressionType.ECUREPRESENTATIVE:
                case EExpressionType.ECUPROGRAMMINGVARIANT:
                    return SingleAssignmentExpression.Deserialize(ms, eExpressionType, vec);
                default:
                    //Log.Error("RuleExpression.Deserialize()", "Unknown Expression-Type");
                    throw new Exception("Unknown Expression-Type");
            }
        }

        public static bool Evaluate(Vehicle vec, RuleExpression exp, IFFMDynamicResolver ffmResolver, ValidationRuleInternalResults internalResult = null)
        {
            if (internalResult == null)
            {
                internalResult = new ValidationRuleInternalResults();
            }
            if (!(exp is AndExpression) && !(exp is OrExpression) && !(exp is CharacteristicExpression) && !(exp is DateExpression) && !(exp is EcuCliqueExpression) && !(exp is NotExpression) && !(exp is SaLaPaExpression) && !(exp is CountryExpression) && !(exp is IStufeExpression) && !(exp is IStufeXExpression) && !(exp is EquipmentExpression) && !(exp is ValidFromExpression) && !(exp is ValidToExpression) && !(exp is SiFaExpression) && !(exp is EcuRepresentativeExpression) && !(exp is ManufactoringDateExpression) && !(exp is EcuVariantExpression) && !(exp is EcuProgrammingVariantExpression))
            {
                //Log.Error("RuleExpression.Evaluate(Vehicle vec, RuleExpression exp)", "RuleExpression {0} not implemented.", exp.ToString());
                return false;
            }
            return exp.Evaluate(vec, ffmResolver, internalResult);
        }

        public static string ParseAndSerializeVariantRule(string rule)
		{
			return RuleExpression.SerializeToString(VariantRuleParser.Parse(rule));
		}

		public static RuleExpression ParseEmpiricalRule(string rule)
		{
			return EmpiricalRuleParser.Parse(rule);
		}

		public static RuleExpression ParseFaultClassRule(string rule)
		{
			return FaultClassRuleParser.Parse(rule);
		}

		public static RuleExpression ParseVariantRule(string rule)
		{
			return VariantRuleParser.Parse(rule);
		}

		public static byte[] SerializeToByteArray(RuleExpression expression)
		{
			MemoryStream memoryStream = new MemoryStream();
			expression.Serialize(memoryStream);
			return memoryStream.ToArray();
		}

		public static string SerializeToString(RuleExpression expression)
		{
			MemoryStream memoryStream = new MemoryStream();
			expression.Serialize(memoryStream);
			return Convert.ToBase64String(memoryStream.GetBuffer(), 0, (int)memoryStream.Length);
		}

		public virtual bool Evaluate(Vehicle vec, IFFMDynamicResolver ffmResolver, ValidationRuleInternalResults internalResult)
		{
			return false;
		}

		public virtual EEvaluationResult EvaluateEmpiricalRule(long[] premises)
		{
			return EEvaluationResult.INVALID;
		}

		public virtual EEvaluationResult EvaluateFaultClassRule(Dictionary<string, List<double>> variables)
		{
			return EEvaluationResult.INVALID;
		}

		public virtual EEvaluationResult EvaluateVariantRule(ClientDefinition client, CharacteristicSet baseConfiguration, EcuConfiguration ecus)
		{
			return EEvaluationResult.INVALID;
		}

		public abstract long GetExpressionCount();

		public abstract long GetMemorySize();

		public virtual IList<long> GetUnknownCharacteristics(CharacteristicSet baseConfiguration)
		{
			return new List<long>();
		}

		public virtual IList<long> GetUnknownVariantIds(EcuConfiguration ecus)
		{
			return new List<long>();
		}

		public virtual void Optimize()
		{
		}

        public virtual string ToFormula(FormulaConfig formulaConfig)
        {
            throw new Exception("ToFormula() missing for class: \"" + this.GetType().Name + "\"");
        }

        public virtual string FormulaSeparator(FormulaConfig formulaConfig)
        {
            if (!string.IsNullOrEmpty(formulaConfig.OperatorSeparator))
            {
                return formulaConfig.OperatorSeparator;
            }

            return string.Empty;
        }

		public abstract void Serialize(MemoryStream ms);

		public static IList<string> RuleEvaluationProtocol;

		public const long MISSING_DATE_EXPRESSION = -1L;

		protected const long MEMORYSIZE_OBJECT = 8L;

		protected const long MEMORYSIZE_REFERENCE = 8L;
	}
}
