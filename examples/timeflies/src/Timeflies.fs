module Timeflies

open Factor.Agent.Types
open Factor.Beam
open Factor.Reactive
open Factor.Reactive.Builder

let text = "TIME FLIES LIKE AN ARROW WITH F# AND FABLE.BEAM"

type MousePos = { X: int; Y: int }

type LetterPos = { Index: int; Char: string; X: int; Y: int }

/// Sets up the reactive pipeline for the timeflies demo.
///
/// Takes a sendFn that sends a JSON string to the WebSocket client.
/// Returns (mouseSender, disposable) where mouseSender receives
/// mouse position events and disposable cleans up the pipeline.
let setupPipeline (sendFn: string -> unit) : Observer<MousePos> * Handle =
    let mouseSender, mouseMoves = Reactive.multicast ()

    let lettersWithIndex =
        text
        |> Seq.toList
        |> List.mapi (fun i c -> i, c)

    let pipeline =
        lettersWithIndex
        |> Reactive.ofList
        |> Reactive.flatMap (fun (index, char) ->
            observable {
                let! (pos: MousePos) = mouseMoves |> Reactive.delay (80 * index)

                return
                    { Index = index
                      Char = string char
                      X = pos.X + index * 14 + 15
                      Y = pos.Y }
            })

    let observer =
        Agent.asObserver (fun lp ->
            let json =
                sprintf "{\"index\":%d,\"char\":\"%s\",\"x\":%d,\"y\":%d}" lp.Index lp.Char lp.X lp.Y

            sendFn json)

    let handle = pipeline |> Reactive.spawn observer

    mouseSender, handle
