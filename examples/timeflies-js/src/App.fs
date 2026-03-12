module App

open Browser.Dom
open Browser.Types
open Feliz
open Fable.Actor
open Fable.Actor.Types

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

let text = "TIME FLIES LIKE AN ARROW"

type MousePos = { X: int; Y: int }

type LetterMsg =
    | MouseMove of MousePos
    | Delayed of MousePos

// ---------------------------------------------------------------------------
// React app
// ---------------------------------------------------------------------------

[<ReactComponent>]
let App () =
    // Mutable positions (actors update this directly)
    let posRef = React.useRef (Array.init text.Length (fun _ -> (-100, -100)))
    // Counter to trigger re-renders
    let _, setTick = React.useState 0

    // Ref to hold the distributor actor
    let distributorRef = React.useRef<Actor<MousePos> option> None

    // Spawn actor pipeline on mount
    React.useEffectOnce (fun () ->
        let mutable tick = 0

        // Each letter gets its own actor with a delay
        let letters =
            text
            |> Seq.toList
            |> List.mapi (fun index _char ->
                spawn (fun inbox ->
                    let rec loop () =
                        actor {
                            let! msg = inbox.Receive()

                            match msg with
                            | MouseMove pos ->
                                schedule (80 * index) (fun () -> send inbox (Delayed pos))
                                |> ignore

                                return! loop ()
                            | Delayed pos ->
                                posRef.current.[index] <- (pos.X + index * 14 + 15, pos.Y)
                                tick <- tick + 1
                                setTick tick
                                return! loop ()
                        }

                    loop ()))

        // Distributor fans out mouse positions to all letter actors
        let distributor =
            spawn (fun inbox ->
                let rec loop () =
                    actor {
                        let! pos = inbox.Receive()
                        letters |> List.iter (fun letter -> send letter (MouseMove pos))
                        return! loop ()
                    }

                loop ())

        distributorRef.current <- Some distributor

        { new System.IDisposable with
            member _.Dispose() =
                kill distributor
                letters |> List.iter kill
        })

    // Listen for mouse moves globally
    React.useEffectOnce (fun () ->
        let handler (ev: Event) =
            let me = ev :?> MouseEvent

            distributorRef.current
            |> Option.iter (fun d ->
                send d { X = int me.clientX; Y = int me.clientY })

        document.addEventListener ("mousemove", handler)

        { new System.IDisposable with
            member _.Dispose() =
                document.removeEventListener ("mousemove", handler)
        })

    Html.div [
        prop.style [
            style.width (length.vw 100)
            style.height (length.vh 100)
        ]
        prop.children [
            for i in 0 .. text.Length - 1 do
                let x, y = posRef.current.[i]

                Html.span [
                    prop.key i
                    prop.text (string text.[i])
                    prop.style [
                        style.position.fixedRelativeToWindow
                        style.left (length.px x)
                        style.top (length.px y)
                        style.color "#00ffff"
                        style.fontSize 18
                        style.fontFamily "monospace"
                        style.fontWeight.bold
                        style.pointerEvents.none
                    ]
                ]
        ]
    ]

// ---------------------------------------------------------------------------
// Mount
// ---------------------------------------------------------------------------

let root =
    ReactDOM.createRoot (document.getElementById "app")

root.render (App())
