"""Bind a preserved live product stream to the real completed command and planned case context."""
import argparse
import json
from pathlib import Path
from qualification import ValidationError, digest, evidence, executed_command, obj, read_json, require, sha256, validate_frame_envelope

def bind(raw, context, command, command_root):
    raw, context = obj(raw,"live output"), obj(context,"predeclared live context")
    require(raw.get("kind") == "frame-envelope" and raw.get("nativeFixtureBindingAvailable") is True,
            "The ordinary live render-owner producer has no verified native fixture binding and cannot be promoted")
    source=digest(raw.get("sourceSha"),"executing live source",40)
    require(context.get("sourceSha")==source,"Live context does not match executing binary")
    require(str(context.get("runtimeFingerprint", "")).lower() == str(raw.get("runtimeFingerprint", "")).lower(), "Live context does not match executing payload")
    require(context.get("role") in ("baseline","candidate"),"Live role missing")
    for field in ("runtimeFingerprint","hardwareIdentitySha256"):
        digest(context.get(field),field)
    require(type(context.get("pair")) is int and context["pair"]>=1 and context.get("caseKey") and context.get("fixtureFingerprint"),"Incomplete planned live workload context")
    success,_=executed_command(command,command_root,source)
    require(success,"Live collection command did not succeed")
    result=dict(raw)
    for field in ("sourceSha","role","pair","caseKey","runtimeFingerprint","hardwareIdentitySha256","fixtureFingerprint"):
        result[field]=context[field]
    result.update(kind="frame-envelope",command=command)
    validate_frame_envelope(result)
    return result

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    for flag in ("telemetry","context","command","output"):
        parser.add_argument("--"+flag,type=Path,required=True)
    args=parser.parse_args()
    try:
        require(not args.output.exists(),"Bound evidence must use a new path")
        require(args.output.parent.resolve()==args.command.parent.resolve(),"Binding output must share command/log directory so original references remain exact")
        result=bind(read_json(args.telemetry),read_json(args.context),read_json(args.command),args.command.parent)
        require(args.telemetry.parent.resolve()==args.output.parent.resolve() and args.context.parent.resolve()==args.output.parent.resolve(),"Preserve original stream/context adjacent to bound command evidence")
        result["rawTelemetry"]={"path":args.telemetry.name,"sha256":sha256(args.telemetry)}
        result["bindingContext"]={"path":args.context.name,"sha256":sha256(args.context)}
        with args.output.open("x",encoding="utf-8") as stream:
            json.dump(result,stream,indent=2,allow_nan=False)
        return 0
    except (ValidationError,OSError,ValueError) as error:
        print(str(error));return 2
if __name__=="__main__":raise SystemExit(main())
