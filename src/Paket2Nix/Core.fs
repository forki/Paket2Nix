(*
  Copyright (C) 2015 Karsten Gebbert

  This program is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  This program is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with this program.  If not, see <http://www.gnu.org/licenses/>.
*)

module Paket2Nix.Core

open System
open System.IO
open System.Security.Cryptography
open Microsoft.FSharp.Control 
open System.Net
open Paket

type Name    = string
type Version = string
type Url     = string
type Sha256  = string
type Rev     = string

(* slightly nicer way of replacing strings *)
let internal replace (from : string) (two : string) (target : string) =
  target.Replace(from,two)

(*
  Template function for a git-based `src` section.
*)
let internal gitTmpl (url : Url) (sha : Sha256) (rev : Rev) =
  @"fetchgit {
    url    = ""$url"";
    sha256 = ""$sha"";
    rev    = ""$rev"";
  }"
  |> replace "$url" url
  |> replace "$sha" sha
  |> replace "$rev" rev

(*
  Template function for a nuget-based `src` section.
*)
let internal nugetTmpl (url : Url) (sha : Sha256) =
  @"fetchurl {
    url    = ""$url"";
    sha256 = ""$sha"";
  }"
  |> replace "$url" url
  |> replace "$sha" sha


(* A type to encode the different available repository methods. *)  
type Method =
  | Nuget  of url : Url * sha256 : Sha256
  | Github of url : Url * sha256 : Sha256 * rev : Rev

  with
    override self.ToString () =
      match self with
        | Nuget(u, s)     -> nugetTmpl u s
        | Github(u, s, r) -> gitTmpl u s r

(*----------------------------------------------------------------------------*)
let internal nixPkgTmpl (name : Name) (version : Version) (meth : Method) =
  @"
with import <nixpkgs> {}:

stdenv.mkDerivation {
  name = ""$pkgname-$version"";

  src = $method;

  phases = [ ""unpackPhase"" ];

  buildInputs = [ unzip ];

  unpackPhase = ''
    mkdir -p ""$out/lib/mono/packages/$pkgname-$version/$name"";
    unzip -x ""$src"" ""$out/lib/mono/packages/$pkgname-$version/$name"";
  '';
}
"
  |> replace "$pkgname" (name.ToLower())
  |> replace "$name"    name
  |> replace "$version" version
  |> replace "$method"  (meth.ToString())

(*----------------------------------------------------------------------------*)
type NixPkg =
  { name    : Name
  ; version : Version
  ; meth    : Method
  ; deps    : NixPkg list
  }
  with
   override self.ToString () =
      nixPkgTmpl self.name self.version self.meth

(*----------------------------------------------------------------------------*)
let parseLockFile path =
  LockFile.LoadFrom path

(*----------------------------------------------------------------------------*)
let fetchSha256 (url : string) : Async<string> = 
  async {
    use wc = new WebClient()
  
    let! bytes = wc.AsyncDownloadData(new Uri(url))

    let sum =
      bytes
      |> HashAlgorithm.Create("SHA256").ComputeHash
      |> BitConverter.ToString
      |> (fun result -> result.Replace("-","").ToLower())

    return sum
  }

(*----------------------------------------------------------------------------*)
let getUrl (pkgres : PackageResolver.ResolvedPackage) : string =
  let version = pkgres.Version.ToString()
  let name =
    match pkgres.Name with
      | Domain.PackageName(_, l) -> l
  sprintf "https://api.nuget.org/packages/%s.%s.nupkg" name version


(*----------------------------------------------------------------------------*)
let pkgToNix (pkgres : PackageResolver.ResolvedPackage) : Async<NixPkg> =
  async {
    let name =
      match pkgres.Name with
        | Domain.PackageName(u, _) -> u

    let version = pkgres.Version.ToString()
    let url = getUrl pkgres

    printfn "downloading resource: %s" url

    let! sha = fetchSha256 url

    return { name    = name
           ; version = version
           ; meth    = Nuget(url, sha)
           ; deps    = List.empty }
  }

(*----------------------------------------------------------------------------*)
let getGroups path = 
  parseLockFile path
  |> (fun file -> Map.toList file.Groups) 

(*----------------------------------------------------------------------------*)
let parseGroup (group : LockFileGroup) : seq<Async<NixPkg>> =
  List.map (snd >> pkgToNix) (Map.toList group.Resolution)
  |> List.toSeq

(*----------------------------------------------------------------------------*)
let paket2Nix path =
  getGroups path
  |> List.toSeq
  |> Seq.map (snd >> parseGroup)
  |> Seq.fold (fun m l -> Seq.append m l) Seq.empty
  |> Async.Parallel
