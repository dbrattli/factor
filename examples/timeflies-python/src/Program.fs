module Program

open Fable.Python.TkInter
open Fable.Python.AsyncIO
open Fable.Actor

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

let text = "TIME FLIES LIKE AN ARROW"

type LetterMsg =
    | MoveTo of x: int * y: int

// ---------------------------------------------------------------------------
// Actor-based pipeline
// ---------------------------------------------------------------------------

/// Each letter actor delays mouse positions by (index * 100ms).
/// Every position gets its own timer — mimics AsyncRx.delay behavior
/// where each letter shows where the mouse was N ms ago.
let letterActor (index: int) (label: Label) =
    spawn (fun inbox ->
        let rec loop () =
            actor {
                let! (MoveTo(x, y)) = inbox.Receive()

                schedule (100 * index) (fun () ->
                    label.place (x + index * 12 + 15, y))
                |> ignore

                return! loop ()
            }

        loop ())

let setupPipeline (root: Tk) =
    let frame = Frame(root, width = 800, height = 600, bg = "#1a1a2e")
    frame.pack ()

    // Spawn a letter actor for each character
    let letters =
        text
        |> Seq.toList
        |> List.mapi (fun i c ->
            let label = Label(frame, text = string c, fg = "#00ffff", bg = "#1a1a2e")
            letterActor i label)

    // Distributor: fans out mouse positions to all letter actors
    let distributor =
        spawn (fun inbox ->
            let rec loop () =
                actor {
                    let! pos = inbox.Receive()
                    letters |> List.iter (fun letter -> send letter pos)
                    return! loop ()
                }

            loop ())

    distributor, frame

// ---------------------------------------------------------------------------
// Main — async event loop with tkinter integration
// ---------------------------------------------------------------------------

let mainAsync =
    async {
        let root = Tk()
        root.title "Fable.Actor Timeflies - Python"

        let distributor, frame = setupPipeline root

        frame.bind (
            "<Motion>",
            fun (ev: Event) ->
                send distributor (MoveTo(ev.x, ev.y))
        )
        |> ignore

        // Async main loop: process tkinter events + yield to asyncio
        while true do
            while root.dooneevent (int Flags.DONT_WAIT) do
                ()

            do! Async.AwaitTask(asyncio.create_task (asyncio.sleep 0.005))
    }

printfn "Started ..."
Async.RunSynchronously mainAsync
