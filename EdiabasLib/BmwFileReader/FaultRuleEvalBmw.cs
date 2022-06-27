﻿using System;
using System.IO;
using System.Reflection;
using EdiabasLib;
using Mono.CSharp;

namespace BmwFileReader
{
    public class FaultRuleEvalBmw
    {
        public delegate string RuleStringDelegate(string name);
        public delegate long RuleNumDelegate(string name);
        public delegate bool IsValidRuleStringDelegate(string name, string value);
        public delegate bool IsValidRuleNumDelegate(string name, long value);

        public object RuleObject { get; private set; }

        public FaultRuleEvalBmw()
        {
            RuleObject = null;
        }

        public bool CreateRuleEvaluator(VehicleStructsBmw.FaultRuleInfo faultRuleInfo, out string errorMessage)
        {
            RuleObject = null;

            errorMessage = string.Empty;
            StringWriter reportWriter = new StringWriter();
            try
            {
                Evaluator evaluator = new Evaluator(new CompilerContext(new CompilerSettings(), new ConsoleReportPrinter(reportWriter)));
                string evalCode =
$@"using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

public class RuleEval
{{
    public delegate string RuleStringDelegate(string name);
    public delegate long RuleNumDelegate(string name);
    public delegate bool IsValidRuleStringDelegate(string name, string value);
    public delegate bool IsValidRuleNumDelegate(string name, long value);

    public RuleStringDelegate RuleStringFunc {{ get; set; }}
    public RuleNumDelegate RuleNumFunc {{ get; set; }}
    public IsValidRuleStringDelegate IsValidRuleStringFunc {{ get; set; }}
    public IsValidRuleNumDelegate IsValidRuleNumFunc {{ get; set; }}

    public RuleEval()
    {{
    }}

    public bool IsRuleValid()
    {{
        return {faultRuleInfo.RuleFormula};
    }}

    private string RuleString(string name)
    {{
        if (RuleStringFunc != null)
        {{
            return RuleStringFunc(name);
        }}
        return string.Empty;
    }}

    private long RuleNum(string name)
    {{
        if (RuleNumFunc != null)
        {{
            return RuleNumFunc(name);
        }}
        return -1;
    }}

    private bool IsValidRuleString(string name, string value)
    {{
        if (IsValidRuleStringFunc != null)
        {{
            return IsValidRuleStringFunc(name, value);
        }}
        return false;
    }}

    private bool IsValidRuleNum(string name, long value)
    {{
        if (IsValidRuleNumFunc != null)
        {{
            return IsValidRuleNumFunc(name, value);
        }}
        return false;
    }}
}}
";
                evaluator.Compile(evalCode);
                object ruleObject = evaluator.Evaluate("new RuleEval()");
                if (ruleObject == null)
                {
                    return false;
                }

                Type ruleType = ruleObject.GetType();
                PropertyInfo propertyRuleString = ruleType.GetProperty("RuleStringFunc");
                if (propertyRuleString != null)
                {
                    propertyRuleString.SetValue(ruleObject, new RuleStringDelegate(RuleString));
                }

                PropertyInfo propertyRuleNum = ruleType.GetProperty("RuleNumFunc");
                if (propertyRuleNum != null)
                {
                    propertyRuleNum.SetValue(ruleObject, new RuleNumDelegate(RuleNum));
                }

                PropertyInfo propertyIsValidRuleString = ruleType.GetProperty("IsValidRuleStringFunc");
                if (propertyIsValidRuleString != null)
                {
                    propertyIsValidRuleString.SetValue(ruleObject, new IsValidRuleStringDelegate(IsValidRuleString));
                }

                PropertyInfo propertyIsValidRuleNum = ruleType.GetProperty("IsValidRuleNumFunc");
                if (propertyIsValidRuleNum != null)
                {
                    propertyIsValidRuleNum.SetValue(ruleObject, new IsValidRuleNumDelegate(IsValidRuleNum));
                }

                RuleObject = ruleObject;

                return true;
            }
            catch (Exception ex)
            {
                RuleObject = null;
                errorMessage = reportWriter.ToString();
                if (string.IsNullOrEmpty(errorMessage))
                {
                    errorMessage = EdiabasNet.GetExceptionText(ex);
                }
                return false;
            }
        }

        public bool ExecuteRuleEvaluator()
        {
            if (RuleObject == null)
            {
                return false;
            }

            try
            {
                Type ruleType = RuleObject.GetType();
                MethodInfo methodIsRuleValid = ruleType.GetMethod("IsRuleValid");
                if (methodIsRuleValid == null)
                {
                    return false;
                }

                // ReSharper disable once UsePatternMatching
                bool? valid = methodIsRuleValid.Invoke(RuleObject, null) as bool?;
                if (!valid.HasValue)
                {
                    return false;
                }

                return valid.Value;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string RuleString(string name)
        {
            return string.Empty;
        }

        private long RuleNum(string name)
        {
            return -1;
        }

        private bool IsValidRuleString(string name, string value)
        {
            return false;
        }

        private bool IsValidRuleNum(string name, long value)
        {
            return false;
        }
    }
}
