﻿namespace KeepAChangelog.Tasks

open Microsoft.Build.Utilities
open Microsoft.Build.Framework
open System.IO
open Ionide.KeepAChangelog
open Ionide.KeepAChangelog.Domain
open System.Linq

module Util =
    let mapReleaseInfo (version: SemVersion.SemanticVersion) (date: System.DateTime) (item: ITaskItem) : ITaskItem =
        item.ItemSpec <- string version
        item.SetMetadata("Date", date.ToString("yyyy-MM-dd"))
        item

    let mapUnreleasedInfo (item: ITaskItem) : ITaskItem =
        item.ItemSpec <- "Unreleased"
        item

    let allReleaseNotesFor (data: ChangelogData) =
        let section name items =
            match items with
            | [] -> []
            | items -> $"### {name}" :: items @ [ "" ]

        String.concat
            System.Environment.NewLine
            (List.concat [ section "Added" data.Added
                           section "Changed" data.Changed
                           section "Deprecated" data.Deprecated
                           section "Removed" data.Removed
                           section "Fixed" data.Fixed
                           section "Security" data.Security ])

    let stitch items =
        String.concat System.Environment.NewLine items

    let mapChangelogData (data: ChangelogData) (item: ITaskItem) : ITaskItem =
        item.SetMetadata("Added", stitch data.Added)
        item.SetMetadata("Changed", stitch data.Changed)
        item.SetMetadata("Deprecated", stitch data.Deprecated)
        item.SetMetadata("Removed", stitch data.Removed)
        item.SetMetadata("Fixed", stitch data.Fixed)
        item.SetMetadata("Security", stitch data.Security)
        item

type ParseChangelogs() =
    inherit Task()

    [<Required>]
    member val ChangelogFile: string = null with get, set

    [<Output>]
    member val UnreleasedChangelog: ITaskItem = null with get, set

    [<Output>]
    member val CurrentReleaseChangelog: ITaskItem = null with get, set

    [<Output>]
    member val AllReleasedChangelogs: ITaskItem [] = null with get, set

    [<Output>]
    member val LatestReleaseNotes: string = null with get, set

    override this.Execute() : bool =
        let file = this.ChangelogFile |> FileInfo

        if not file.Exists then
            this.Log.LogError($"The file {file.FullName} could not be found.")
            false
        else
            match Parser.parseChangeLog file with
            | Ok changelogs ->
                changelogs.Unreleased
                |> Option.iter (fun unreleased ->
                    this.UnreleasedChangelog <-
                        TaskItem()
                        |> Util.mapChangelogData unreleased
                        |> Util.mapUnreleasedInfo)

                let sortedReleases =
                    // have to use LINQ here because List.sortBy* require IComparable, which
                    // semver doesn't implement
                    changelogs.Releases.OrderByDescending(fun (v, _, _) -> v)

                let items =
                    sortedReleases
                    |> Seq.map (fun (version, date, data) ->
                        TaskItem()
                        |> Util.mapChangelogData data
                        |> Util.mapReleaseInfo version date)
                    |> Seq.toArray

                this.AllReleasedChangelogs <- items
                this.CurrentReleaseChangelog <- items.FirstOrDefault()

                sortedReleases
                |> Seq.tryHead
                |> Option.iter (fun (version, date, data) -> this.LatestReleaseNotes <- Util.allReleaseNotesFor data)

                true
            | Error (formatted, msg) ->

                this.Log.LogError(
                    $"Error parsing Changelog at {file.FullName}. The error occurred at {msg.Position}.{System.Environment.NewLine}{formatted}"
                )

                false
