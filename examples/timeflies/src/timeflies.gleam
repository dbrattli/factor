//// Timeflies Demo - Classic Rx example with ActorX and WebSockets
////
//// Letters of "TIME FLIES LIKE AN ARROW" follow the mouse cursor,
//// with each letter delayed by an increasing amount.

import actorx
import actorx/types.{type Observer, Disposable, Observer, OnCompleted, OnNext}
import gleam/bytes_tree
import gleam/dynamic/decode
import gleam/erlang/process.{type Subject}
import gleam/http/request.{type Request}
import gleam/http/response
import gleam/io
import gleam/json
import gleam/list
import gleam/option.{Some}
import gleam/result
import gleam/string
import mist.{type WebsocketConnection, type WebsocketMessage}

const text = "TIME FLIES LIKE AN ARROW"

const html = "<!DOCTYPE html>
<html>
<head>
  <title>Timeflies - ActorX Demo</title>
  <style>
    body {
      margin: 0;
      padding: 0;
      background: #1a1a2e;
      overflow: hidden;
      font-family: 'Courier New', monospace;
      cursor: crosshair;
    }
    .letter {
      position: absolute;
      font-size: 24px;
      font-weight: bold;
      color: #eee;
      text-shadow: 0 0 10px #0ff, 0 0 20px #0ff;
      pointer-events: none;
      transition: text-shadow 0.1s;
    }
    #info {
      position: fixed;
      bottom: 20px;
      left: 20px;
      color: #666;
      font-size: 14px;
    }
    #title {
      position: fixed;
      top: 20px;
      left: 20px;
      color: #0ff;
      font-size: 18px;
    }
  </style>
</head>
<body>
  <div id=\"title\">ActorX Timeflies Demo</div>
  <div id=\"info\">Move your mouse...</div>
  <div id=\"letters\"></div>

  <script>
    const text = 'TIME FLIES LIKE AN ARROW';
    const container = document.getElementById('letters');
    const letters = [];

    // Create letter elements
    for (let i = 0; i < text.length; i++) {
      const span = document.createElement('span');
      span.className = 'letter';
      span.textContent = text[i];
      span.style.left = '-100px';
      span.style.top = '-100px';
      container.appendChild(span);
      letters.push(span);
    }

    // Connect WebSocket
    const ws = new WebSocket(`ws://${window.location.host}/ws`);

    ws.onopen = () => {
      document.getElementById('info').textContent = 'Connected! Move your mouse...';
    };

    ws.onmessage = (event) => {
      const data = JSON.parse(event.data);
      if (data.index !== undefined && letters[data.index]) {
        letters[data.index].style.left = data.x + 'px';
        letters[data.index].style.top = data.y + 'px';
      }
    };

    ws.onclose = () => {
      document.getElementById('info').textContent = 'Disconnected. Refresh to reconnect.';
    };

    // Send mouse moves
    let lastSend = 0;
    document.addEventListener('mousemove', (e) => {
      const now = Date.now();
      if (now - lastSend > 16 && ws.readyState === WebSocket.OPEN) { // ~60fps throttle
        ws.send(JSON.stringify({ x: e.clientX, y: e.clientY }));
        lastSend = now;
      }
    });
  </script>
</body>
</html>"

/// Mouse position from client
type MousePos {
  MousePos(x: Int, y: Int)
}

/// Letter position to send back to client
type LetterPos {
  LetterPos(index: Int, x: Int, y: Int)
}

/// WebSocket state holds the input subject and disposal
type WsState {
  WsState(input: Observer(MousePos), dispose: fn() -> Nil)
}

pub fn main() {
  let handler = fn(req: Request(mist.Connection)) {
    case request.path_segments(req) {
      [] -> serve_html()
      ["ws"] -> handle_websocket(req)
      _ -> not_found()
    }
  }

  let assert Ok(_) =
    mist.new(handler)
    |> mist.port(3000)
    |> mist.start

  io.println("Timeflies demo running at http://localhost:3000")

  // Start Erlang Observer GUI for monitoring
  observer_start()

  process.sleep_forever()
}

@external(erlang, "observer", "start")
fn observer_start() -> Nil

fn serve_html() -> response.Response(mist.ResponseData) {
  response.new(200)
  |> response.set_header("content-type", "text/html")
  |> response.set_body(mist.Bytes(bytes_tree.from_string(html)))
}

fn not_found() -> response.Response(mist.ResponseData) {
  response.new(404)
  |> response.set_body(mist.Bytes(bytes_tree.from_string("Not found")))
}

fn handle_websocket(
  req: Request(mist.Connection),
) -> response.Response(mist.ResponseData) {
  mist.websocket(
    request: req,
    on_init: on_ws_init,
    on_close: on_ws_close,
    handler: on_ws_message,
  )
}

fn on_ws_init(
  _conn: WebsocketConnection,
) -> #(WsState, option.Option(process.Selector(LetterPos))) {
  // Create a multicast subject for mouse positions (allows multiple subscribers)
  let #(input, mouse_moves) = actorx.subject()

  // Create the Timeflies stream
  // For each letter, delay the mouse stream and add the letter's offset
  let letters_with_index =
    text
    |> string.to_graphemes
    |> list.index_map(fn(char, i) { #(i, char) })

  let stream =
    actorx.from_list(letters_with_index)
    |> actorx.flat_map(fn(pair) {
      let #(index, _char) = pair
      mouse_moves
      |> actorx.delay(80 * index)
      |> actorx.map(fn(pos: MousePos) {
        LetterPos(index: index, x: pos.x + index * 14 + 15, y: pos.y)
      })
    })

  // Create a subject to receive transformed positions
  let output_subject: Subject(LetterPos) = process.new_subject()

  // Subscribe the stream to send to our output subject
  let observer: Observer(LetterPos) =
    Observer(notify: fn(notification) {
      case notification {
        OnNext(letter_pos) -> process.send(output_subject, letter_pos)
        OnCompleted | types.OnError(_) -> Nil
      }
    })

  let Disposable(dispose) = actorx.subscribe(stream, observer)

  // Create selector to receive LetterPos messages from the stream
  // These will arrive as mist.Custom(LetterPos) in on_ws_message
  let selector =
    process.new_selector()
    |> process.select(output_subject)

  let state = WsState(input: input, dispose: dispose)
  #(state, Some(selector))
}

fn on_ws_close(state: WsState) -> Nil {
  state.dispose()
}

fn on_ws_message(
  state: WsState,
  message: WebsocketMessage(LetterPos),
  conn: WebsocketConnection,
) -> mist.Next(WsState, LetterPos) {
  case message {
    mist.Text(json_str) -> {
      case parse_mouse_pos(json_str) {
        Ok(pos) -> actorx.on_next(state.input, pos)
        Error(_) -> Nil
      }
      mist.continue(state)
    }
    mist.Custom(letter_pos) -> {
      // Received transformed position from the stream - send to client
      let json_str =
        json.object([
          #("index", json.int(letter_pos.index)),
          #("x", json.int(letter_pos.x)),
          #("y", json.int(letter_pos.y)),
        ])
        |> json.to_string

      let _ = mist.send_text_frame(conn, json_str)
      mist.continue(state)
    }
    mist.Closed | mist.Shutdown -> mist.stop()
    mist.Binary(_) -> mist.continue(state)
  }
}

fn parse_mouse_pos(json_str: String) -> Result(MousePos, Nil) {
  json.parse(json_str, mouse_pos_decoder())
  |> result.replace_error(Nil)
}

fn mouse_pos_decoder() -> decode.Decoder(MousePos) {
  use x <- decode.field("x", decode.int)
  use y <- decode.field("y", decode.int)
  decode.success(MousePos(x: x, y: y))
}
