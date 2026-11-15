// EasyPDM plugin for SolidCraft.
//
// SolidCraft plugins run as WebAssembly components with no network at all (RFC 0002 reserves
// `net` for signed plugins from a future curated index; see tangency-labs/solidcraft#17343), so
// this plugin never talks to the EasyPDM server. It prepares what an upload needs -- the design
// as .sc, a STEP of it, its mass and its design properties -- in a folder the user granted at
// install time (the "outbox"), together with a request file. The EasyPDM companion program on
// the same computer picks the request up and does the HTTP, exactly like the FreeCAD and
// SolidWorks macros do it themselves.
//
// Every file a call writes is staged by the host and published only when the call succeeds,
// so a failed upload leaves no half-written request behind. The request is written LAST, after
// the files it names, but the host publishes all of them together anyway.

wit_bindgen::generate!({
    path: "wit",
    world: "command-plugin",
});

use craft::plugin::{clock, commands, files, log};
use craft::plugin::types::{CommandFailure, FieldFailure, PluginError};
use exports::craft::plugin::entry::Guest;
use serde_json::{json, Value};

const OUTBOX: &str = "outbox";
// Where EasyPDM keeps its link to the PDM item inside the design (a reverse-DNS key holding an
// object, as SolidCraft asks other tools to do); it survives saving and reopening the design.
const META_KEY: &str = "io.github.pawelcel.easypdm";

struct Plugin;

fn run(command: &str, args: Value) -> Result<Value, PluginError> {
    let reply = commands::run(command, &args.to_string())?;
    serde_json::from_str(&reply).map_err(|e| {
        PluginError::CommandFailed(CommandFailure {
            command: command.to_string(),
            reason: format!("reply is not JSON: {e}"),
        })
    })
}

fn invalid(field: &str, reason: &str) -> PluginError {
    PluginError::InvalidInput(FieldFailure { field: field.into(), reason: reason.into() })
}

// A file-name-safe stamp for this request: the host clock (needs the `clock` permission;
// without it the clock reads a fixed epoch, which would make every request collide).
fn stamp() -> String {
    format!("{}", clock::now())
}

fn upload(params: &Value) -> Result<Value, PluginError> {
    let meta = run("document.metadata", json!({}))?;
    let props = meta.get("properties").cloned().unwrap_or(Value::Null);
    let link = meta.get("metadata").and_then(|m| m.get(META_KEY)).cloned().unwrap_or(Value::Null);

    let id = stamp();
    let design = format!("{id}.sc");
    let step = format!("{id}.step");
    // file.* commands see exactly the granted folders; the companion knows the outbox's
    // absolute path from the same setting, so paths are passed as file names inside it.
    let outbox_path = params
        .get("outbox_path")
        .and_then(Value::as_str)
        .ok_or_else(|| invalid("outbox_path", "the outbox folder is not set up"))?;
    let sep = if outbox_path.contains('\\') { "\\" } else { "/" };
    let design_path = format!("{outbox_path}{sep}{design}");
    let step_path = format!("{outbox_path}{sep}{step}");

    log::log(log::Level::Info, &format!("EasyPDM: preparing upload {id}"));
    run("file.save_as", json!({ "path": design_path, "cache": true }))?;
    run("file.export_step", json!({ "path": step_path }))?;
    let mass = run("assembly.mass_properties", json!({}))
        .ok()
        .and_then(|m| m.get("mass").and_then(|x| x.get("mass_kg")).cloned())
        .unwrap_or(Value::Null);

    let request = json!({
        "kind": "upload",
        "id": id,
        "design": design,
        "step": step,
        "mass_kg": mass,
        "properties": props,
        "easypdm": link,
        "comment": params.get("comment").cloned().unwrap_or(Value::Null),
    });
    files::write_file(OUTBOX, &format!("{id}.request.json"), request.to_string().as_bytes())?;
    Ok(json!({ "queued": id, "message": "Sent to EasyPDM. The companion program uploads it." }))
}

impl Guest for Plugin {
    fn execute(entry: String, params: String) -> Result<String, PluginError> {
        let params: Value = serde_json::from_str(if params.is_empty() { "{}" } else { &params })
            .map_err(|e| invalid("params", &e.to_string()))?;
        let result = match entry.as_str() {
            "upload" => upload(&params)?,
            other => return Err(invalid("entry", &format!("unknown entry {other}"))),
        };
        Ok(result.to_string())
    }
}

export!(Plugin);
