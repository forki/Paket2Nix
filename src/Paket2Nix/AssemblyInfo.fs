﻿namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("Paket2Nix")>]
[<assembly: AssemblyProductAttribute("Paket2Nix")>]
[<assembly: AssemblyDescriptionAttribute("Convert Paket Projects Into Nix Expressions")>]
[<assembly: AssemblyVersionAttribute("1.0.2.0")>]
[<assembly: AssemblyFileVersionAttribute("1.0.2.0")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "1.0.2.0"
