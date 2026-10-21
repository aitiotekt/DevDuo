import AVFoundation
import AppKit
import CryptoKit
import Foundation

let arguments = CommandLine.arguments
if arguments.count != 3 {
  fputs(
    "usage: swift run --package-path scripts/swift devduo-inspect-video video.mov NEW_OUTPUT_DIRECTORY\n",
    stderr)
  exit(2)
}
let source = URL(fileURLWithPath: arguments[1])
let output = URL(fileURLWithPath: arguments[2], isDirectory: true)
guard !FileManager.default.fileExists(atPath: output.path) else {
  fputs("Output already exists; choose a new directory to preserve prior evidence.\n", stderr)
  exit(2)
}
do {
  let sourceData = try Data(contentsOf: source, options: .mappedIfSafe)
  let sourceHash = SHA256.hash(data: sourceData).map { String(format: "%02x", $0) }.joined()
  try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
  let asset = AVURLAsset(url: source)
  let duration = try await asset.load(.duration)
  let seconds = CMTimeGetSeconds(duration)
  guard seconds.isFinite, seconds > 0 else {
    throw NSError(
      domain: "DevDuo", code: 1, userInfo: [NSLocalizedDescriptionKey: "Invalid video duration"])
  }
  let tracks = try await asset.loadTracks(withMediaType: .video)
  guard let track = tracks.first else {
    throw NSError(
      domain: "DevDuo", code: 2, userInfo: [NSLocalizedDescriptionKey: "No video track"])
  }
  let rate = try await track.load(.nominalFrameRate)
  let natural = try await track.load(.naturalSize)
  let generator = AVAssetImageGenerator(asset: asset)
  generator.appliesPreferredTrackTransform = true
  var frames: [[String: Any]] = []
  let times = [
    0.0, seconds * 0.5, max(0, seconds - 1.5), max(0, seconds - 0.7), max(0, seconds - 0.1),
  ]
  for (index, t) in times.enumerated() {
    let result = try await generator.image(at: CMTime(seconds: t, preferredTimescale: 600))
    let bitmap = NSBitmapImageRep(cgImage: result.image)
    let name = String(format: "frame-%02d.png", index)
    guard let data = bitmap.representation(using: .png, properties: [:]) else {
      throw NSError(domain: "DevDuo", code: 3)
    }
    try data.write(to: output.appendingPathComponent(name))
    frames.append([
      "image": name, "sha256": SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined(),
      "requestedSeconds": t, "actualSeconds": CMTimeGetSeconds(result.actualTime),
      "widthPx": result.image.width, "heightPx": result.image.height,
    ])
  }
  let report: [String: Any] = [
    "schemaVersion": 1, "source": source.path, "sourceSha256": sourceHash,
    "durationSeconds": seconds,
    "nominalFramesPerSecond": rate,
    "naturalTrackSizePx": ["width": natural.width, "height": natural.height],
    "appliesPreferredTrackTransform": true, "frames": frames,
    "note":
      "Frames are decoded from actual simulator video. Nominal fps is encoded metadata, not proof of compositor timing or physical display refresh.",
  ]
  let data = try JSONSerialization.data(
    withJSONObject: report, options: [.prettyPrinted, .sortedKeys])
  try data.write(to: output.appendingPathComponent("video-metadata.json"))
  print(String(data: data, encoding: .utf8)!)

} catch {
  fputs("devduo-inspect-video: \(error.localizedDescription)\n", stderr)
  exit(1)
}
