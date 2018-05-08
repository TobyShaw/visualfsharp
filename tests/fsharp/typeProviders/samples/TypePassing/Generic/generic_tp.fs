namespace Test

open System
open System.Reflection
open Microsoft.FSharp.Core.CompilerServices
open ProviderImplementation.ProvidedTypes
open System.Collections
open System.Collections.Generic
open Microsoft.FSharp.Quotations

[<TypeProvider>]
type TypePassingTp(config: TypeProviderConfig) as this =
    inherit TypeProviderForNamespaces(config)

    let ns = "Generic"
    let runtimeAssembly = Assembly.LoadFrom(config.RuntimeAssembly)

    // ====== Generic Method ========

    let genericMethodType = ProvidedTypeDefinition(runtimeAssembly, ns, "IdentityMethod", baseType = Some typeof<obj>, hideObjectMethods=true)

    let createMethod (t : Type) (name : string) : ProvidedMethod =
        let invoke (xs : Quotations.Expr list) =
            xs.[0]
        let m = ProvidedMethod(name, [ProvidedParameter("x", t)], t, invoke, isStatic = true)
        genericMethodType.AddMember(m)
        m

    let builder = ProvidedMethod("Create", [], genericMethodType, Unchecked.defaultof<_>, isStatic = true)
    do builder.DefineStaticParameters(
        [
            ProvidedStaticParameter("Type",typeof<Type>, null)
        ], fun typeName args -> createMethod (unbox args.[0]) typeName)

    do genericMethodType.AddMember(builder)

    // ====== Generic Types ========

    let idType = ProvidedTypeDefinition(runtimeAssembly, ns, "IdentityType", baseType = Some typeof<obj>, hideObjectMethods=true)

    let createIdType (t : Type) (name : string) : ProvidedTypeDefinition =
        let invoke (xs : Quotations.Expr list) =
            xs.[0]
        let newType = ProvidedTypeDefinition(runtimeAssembly, ns, name, baseType = Some typeof<obj>, hideObjectMethods = true)
        let m = ProvidedMethod("Invoke", [ProvidedParameter("x", t)], t, invoke, isStatic = true)
        newType.AddMember(m)
        newType

    do idType.DefineStaticParameters(
        [
            ProvidedStaticParameter("Type",typeof<Type>, null)
        ], fun typeName args -> createIdType (unbox args.[0]) typeName)

    let constType = ProvidedTypeDefinition(runtimeAssembly, ns, "ConstType", baseType = Some typeof<obj>, hideObjectMethods=true)

    let createIdType (t1 : Type) (t2 : Type) (name : string) : ProvidedTypeDefinition =
        let invoke (xs : Quotations.Expr list) =
            xs.[0]
        let newType = ProvidedTypeDefinition(runtimeAssembly, ns, name, baseType = Some typeof<obj>, hideObjectMethods = true)
        let m = ProvidedMethod("Invoke", [ProvidedParameter("x", t1); ProvidedParameter("y", t2)], t1, invoke, isStatic = true)
        newType.AddMember(m)
        newType

    do constType.DefineStaticParameters(
        [
            ProvidedStaticParameter("Type1",typeof<Type>, null)
            ProvidedStaticParameter("Type2",typeof<Type>, null)
        ], fun typeName args -> createIdType (unbox args.[0]) (unbox args.[1]) typeName)

    // ===========================

    do this.AddNamespace(ns, [genericMethodType; idType; constType])

[<assembly:TypeProviderAssembly>]
do()

