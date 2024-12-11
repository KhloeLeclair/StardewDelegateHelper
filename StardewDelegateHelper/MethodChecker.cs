using System;
using System.Diagnostics.CodeAnalysis;

using Microsoft.CodeAnalysis;

using StardewDelegateHelper.SystemStuff;

namespace StardewDelegateHelper;

internal readonly struct MethodChecker(
	ITypeSymbol? returnType,
	EquatableArray<MethodParameter>? parameters,
	bool invalid = false
) : IEquatable<MethodChecker> {

	public static MethodChecker Invalid = new(null, null, true);

	public readonly ITypeSymbol? ReturnType = returnType;
	public readonly EquatableArray<MethodParameter> Parameters = parameters ?? [];
	public readonly bool IsInvalid = invalid;

	public bool Equals(MethodChecker other) {
		return SymbolEqualityComparer.Default.Equals(ReturnType, other.ReturnType) && Parameters.Equals(other.Parameters) && IsInvalid == other.IsInvalid;
	}

	public bool Matches(IMethodSymbol method, [NotNullWhen(false)] out string? error) {
		if (IsInvalid) {
			error = "Unable to find delegate for comparison.";
			return false;
		}

		if (ReturnType != null && !SymbolEqualityComparer.Default.Equals(method.ReturnType, ReturnType)) {
			error = $"Return type doesn't match, wanted '{ReturnType.ToDisplayString()}' but got '{method.ReturnType.ToDisplayString()}'";
			return false;
		}

		for (int i = 0; i < Parameters.Length; i++) {
			var param = method.Parameters.Length > i ? method.Parameters[i] : null;
			var paramCheck = Parameters[i];

			if (!paramCheck.Matches(param, out error)) {
				if (paramCheck.Name != null)
					error = $"Parameter '{paramCheck.Name}' at {i} doesn't match: {error}";
				else
					error = $"Parameter at {i} doesn't match: {error}";
				return false;
			}
		}

		error = null;
		return true;
	}

	public static MethodChecker Create(params ITypeSymbol?[] paramTypes) {
		MethodParameter[] parms = new MethodParameter[paramTypes.Length];
		for (int i = 0; i < paramTypes.Length; i++) {
			var paramType = paramTypes[i];
			if (paramType == null)
				return Invalid;

			parms[i] = new MethodParameter(null, paramType, RefKind.None);
		}

		return new(null, parms.AsEquatable());
	}

	public static MethodChecker CreateWithReturnType(ITypeSymbol? returnType, params ITypeSymbol?[] paramTypes) {
		if (returnType is null)
			return Invalid;

		MethodParameter[] parms = new MethodParameter[paramTypes.Length];
		for (int i = 0; i < paramTypes.Length; i++) {
			var paramType = paramTypes[i];
			if (paramType == null)
				return Invalid;

			parms[i] = new MethodParameter(null, paramType, RefKind.None);
		}

		return new(returnType, parms.AsEquatable());
	}

	public static MethodChecker FromMethod(IMethodSymbol method) {
		MethodParameter[] parms = new MethodParameter[method.Parameters.Length];
		for (int i = 0; i < parms.Length; i++) {
			var parameter = method.Parameters[i];
			parms[i] = new(parameter.Name, parameter.Type, parameter.RefKind);
		}

		return new(method.ReturnType, parms.AsEquatable());
	}

	public static MethodChecker FromDelegate(Compilation compilation, string typeName) {
		var type = compilation.GetTypeByMetadataName(typeName);
		if (type?.DelegateInvokeMethod is IMethodSymbol method)
			return FromMethod(method);
		return Invalid;
	}

}


internal readonly struct MethodParameter(
	string? name,
	ITypeSymbol type,
	RefKind refKind
) : IEquatable<MethodParameter> {

	public readonly string? Name = name;
	public readonly ITypeSymbol Type = type;
	public readonly RefKind RefKind = refKind;

	public bool Equals(MethodParameter other) {
		return Name == other.Name && SymbolEqualityComparer.Default.Equals(Type, other.Type) && other.RefKind == RefKind;
	}

	public bool Matches(IParameterSymbol? parm, [NotNullWhen(false)] out string? error) {
		if (parm is null) {
			error = $"Missing, wanted '{Type.ToDisplayString()}'";
			return false;
		}

		if (!SymbolEqualityComparer.Default.Equals(Type, parm.Type)) {
			error = $"Type doesn't match, wanted '{Type.ToDisplayString()}' but got '{parm.Type.ToDisplayString()}'";
			return false;
		}

		if (parm.RefKind != RefKind) {
			error = $"RefKind doesn't match, wanted '{RefKind}' but got '{parm.RefKind}'";
			return false;
		}

		error = null;
		return true;
	}

}
