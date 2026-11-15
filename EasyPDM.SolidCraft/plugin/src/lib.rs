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

// The host clock in whole seconds (needs the `clock` permission; without it the clock reads a
// fixed epoch).
fn now() -> u64 {
    clock::now()
}

// The design's link to its EasyPDM item, kept in the design under META_KEY:
//   {"designId": "...", "itemId": "...", "recordName": "...", "revision": "B"}
// "designId" is given on the first upload and never changes. The item id is not known then --
// the item is chosen in the browser, by the companion -- so the plugin also writes
// "<designId>.link.json" into the outbox, and the companion fills the item into that file once
// the upload has gone through. The next upload reads it back into the design.
//
// The plugin reads ONLY that file, and only while the design has a designId but no itemId yet:
// SolidCraft voids the whole call when a plugin's read fails, even a read the plugin handles,
// so it must never ask for a file that may not exist. The link file exists because this plugin
// wrote it in an earlier successful call; once the item id is in the design, nothing is read,
// which is also what lets a linked design be uploaded from another computer.
fn link_file(design_id: &str) -> String {
    format!("{design_id}.link.json")
}

fn upload(params: &Value) -> Result<Value, PluginError> {
    let meta = run("document.metadata", json!({}))?;
    let mut props = meta.get("properties").cloned().unwrap_or(Value::Null);
    let mut link = meta
        .get("metadata")
        .and_then(|m| m.get(META_KEY))
        .and_then(Value::as_object)
        .cloned()
        .unwrap_or_default();

    let now = now();
    let mut values = serde_json::Map::new();
    match link.get("designId").and_then(Value::as_str).map(str::to_string) {
        None => {
            link.insert("designId".into(), json!(format!("d{now}")));
        }
        Some(design_id) if link.get("itemId").is_none() => {
            let bytes = files::read_file(OUTBOX, &link_file(&design_id))?;
            let known: Value = serde_json::from_slice(&bytes).unwrap_or(Value::Null);
            if let Some(item) = known.get("itemId").filter(|v| v.is_string()) {
                link.insert("itemId".into(), item.clone());
                for (key, prop) in [("recordName", "part_number"), ("revision", "revision")] {
                    if let Some(v) = known.get(key).filter(|v| v.is_string()) {
                        link.insert(key.into(), v.clone());
                        values.insert(prop.into(), v.clone());
                    }
                }
            }
        }
        Some(_) => {}
    }
    let design_id = link["designId"].as_str().unwrap_or_default().to_string();
    // The request id: the clock alone repeats within a second -- two quick uploads got the same
    // id and the second silently replaced the first's files -- so it also carries the design and
    // a per-design counter kept in the link. It still sorts by time, which is the order the
    // companion uploads in.
    let seq = link.get("seq").and_then(Value::as_u64).unwrap_or(0) + 1;
    link.insert("seq".into(), json!(seq));
    let id = format!("{now}-{design_id}-{seq:04}");
    values.insert(META_KEY.into(), Value::Object(link.clone()));
    // Into the user's design (one undo step with the rest of this call): the link, and once the
    // item is known its record name and revision as SolidCraft's own part number and revision.
    run("document.metadata.set", json!({ "values": values }))?;
    if let Value::Object(p) = &mut props {
        for (k, v) in &values {
            if k != META_KEY {
                p.insert(k.clone(), v.clone());
            }
        }
    }

    let design = format!("{id}.sc");
    let step = format!("{id}.step");
    // file.* commands take absolute paths within the granted folder.
    let outbox_path = params
        .get("outbox_path")
        .and_then(Value::as_str)
        .ok_or_else(|| invalid("outbox_path", "the outbox folder is not set up"))?;
    let sep = if outbox_path.contains('\\') { "\\" } else { "/" };

    log::log(log::Level::Info, &format!("EasyPDM: preparing upload {id}"));
    run("file.save_as", json!({ "path": format!("{outbox_path}{sep}{design}"), "cache": true }))?;
    run("file.export_step", json!({ "path": format!("{outbox_path}{sep}{step}") }))?;

    if link.get("itemId").is_none() {
        // Created now, so a later upload can read it; the companion adds the item to it.
        files::write_file(OUTBOX, &link_file(&design_id), json!({ "designId": design_id }).to_string().as_bytes())?;
    }
    let request = json!({
        "kind": "upload",
        "id": id,
        "design": design,
        "step": step,
        "properties": props,
        "easypdm": Value::Object(link),
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
