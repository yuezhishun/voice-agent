#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
REPO_ROOT=$(cd "${SCRIPT_DIR}/.." && pwd)

COUNT=200
SEED=20260207
AUTO_DOWNLOAD=1
INPUT_DIR=""
WORK_DIR="${REPO_ROOT}/data/slr31"
OUTPUT_DIR="${REPO_ROOT}/src/VoiceAgent.AsrMvp.Tests/TestAssets/librispeech-dev-clean-2-200"

DEV_CLEAN_2_URL="https://www.openslr.org/resources/31/dev-clean-2.tar.gz"

usage() {
  cat <<USAGE
Usage: $(basename "$0") [options]

Generate a fixed-size sample set from Mini LibriSpeech dev-clean-2.

Options:
  --count N            Number of audio files to sample (default: ${COUNT})
  --seed N             Random seed for deterministic sampling (default: ${SEED})
  --input-dir PATH     Existing extracted dev-clean-2 directory
                       (example: /path/to/LibriSpeech/dev-clean-2)
  --output-dir PATH    Output directory (default: ${OUTPUT_DIR})
  --work-dir PATH      Download/extract workspace (default: ${WORK_DIR})
  --no-download        Do not auto-download when input-dir is missing
  -h, --help           Show this help

Output structure:
  <output-dir>/audio/*.wav
  <output-dir>/manifest.tsv

WAV format:
  mono, 16kHz, PCM16
USAGE
}

require_cmd() {
  local cmd="$1"
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "ERROR: required command not found: ${cmd}" >&2
    exit 1
  fi
}

resolve_input_dir() {
  local candidate

  if [[ -n "${INPUT_DIR}" ]]; then
    if [[ -d "${INPUT_DIR}" ]]; then
      echo "${INPUT_DIR}"
      return
    fi

    echo "ERROR: --input-dir does not exist: ${INPUT_DIR}" >&2
    exit 1
  fi

  candidate="${WORK_DIR}/LibriSpeech/dev-clean-2"
  if [[ -d "${candidate}" ]]; then
    echo "${candidate}"
    return
  fi

  if [[ "${AUTO_DOWNLOAD}" != "1" ]]; then
    echo "ERROR: dev-clean-2 not found. Provide --input-dir or remove --no-download." >&2
    exit 1
  fi

  require_cmd curl
  require_cmd tar

  mkdir -p "${WORK_DIR}"
  local archive="${WORK_DIR}/dev-clean-2.tar.gz"

  echo "[1/5] Downloading dev-clean-2 ..." >&2
  curl -fL "${DEV_CLEAN_2_URL}" -o "${archive}"

  echo "[2/5] Extracting dev-clean-2 ..." >&2
  tar -xzf "${archive}" -C "${WORK_DIR}"

  if [[ ! -d "${candidate}" ]]; then
    echo "ERROR: extracted directory not found: ${candidate}" >&2
    exit 1
  fi

  echo "${candidate}"
}

parse_args() {
  while [[ $# -gt 0 ]]; do
    case "$1" in
      --count)
        COUNT="$2"
        shift 2
        ;;
      --seed)
        SEED="$2"
        shift 2
        ;;
      --input-dir)
        INPUT_DIR="$2"
        shift 2
        ;;
      --output-dir)
        OUTPUT_DIR="$2"
        shift 2
        ;;
      --work-dir)
        WORK_DIR="$2"
        shift 2
        ;;
      --no-download)
        AUTO_DOWNLOAD=0
        shift
        ;;
      -h|--help)
        usage
        exit 0
        ;;
      *)
        echo "ERROR: unknown option: $1" >&2
        usage >&2
        exit 1
        ;;
    esac
  done

  if ! [[ "${COUNT}" =~ ^[0-9]+$ ]] || [[ "${COUNT}" -le 0 ]]; then
    echo "ERROR: --count must be a positive integer" >&2
    exit 1
  fi

  if ! [[ "${SEED}" =~ ^[0-9]+$ ]]; then
    echo "ERROR: --seed must be an integer" >&2
    exit 1
  fi
}

transcode_to_wav() {
  local src="$1"
  local dst="$2"

  if command -v ffmpeg >/dev/null 2>&1; then
    ffmpeg -hide_banner -loglevel error -nostdin -y \
      -i "${src}" -ac 1 -ar 16000 -sample_fmt s16 "${dst}"
    return
  fi

  if command -v sox >/dev/null 2>&1; then
    sox "${src}" -c 1 -r 16000 -b 16 -e signed-integer "${dst}"
    return
  fi

  echo "ERROR: neither ffmpeg nor sox found; cannot convert FLAC to WAV" >&2
  exit 1
}

main() {
  parse_args "$@"
  require_cmd find
  require_cmd shuf
  require_cmd awk

  local source_dir
  source_dir=$(resolve_input_dir)

  mapfile -t all_flac < <(find "${source_dir}" -type f -name '*.flac' | sort)
  local total="${#all_flac[@]}"
  if [[ "${total}" -eq 0 ]]; then
    echo "ERROR: no .flac files found under ${source_dir}" >&2
    exit 1
  fi

  if [[ "${COUNT}" -gt "${total}" ]]; then
    echo "ERROR: requested --count ${COUNT} exceeds available files ${total}" >&2
    exit 1
  fi

  mkdir -p "${OUTPUT_DIR}/audio"
  rm -f "${OUTPUT_DIR}/audio"/*.wav

  local manifest="${OUTPUT_DIR}/manifest.tsv"
  cat > "${manifest}" <<'HEADER'
caseId	bucket	audioPath	referenceText	expectedFinalReason	allowFallback	expectInterrupt	notes	sourceFlac
HEADER

  declare -A transcript_by_id
  while IFS= read -r -d '' trans_file; do
    while IFS= read -r line || [[ -n "${line}" ]]; do
      [[ -z "${line}" ]] && continue
      local utt_id="${line%% *}"
      local text="${line#* }"
      transcript_by_id["${utt_id}"]="${text}"
    done < "${trans_file}"
  done < <(find "${source_dir}" -type f -name '*.trans.txt' -print0)

  local random_source
  random_source=$(mktemp)
  trap 'rm -f "${random_source:-}"' EXIT

  awk -v seed="${SEED}" 'BEGIN { srand(seed); for (i = 0; i < 1048576; i++) printf "%c", int(rand() * 256); }' > "${random_source}"

  mapfile -t sampled < <(
    printf '%s\n' "${all_flac[@]}" | shuf --random-source="${random_source}" | head -n "${COUNT}"
  )

  echo "[3/5] Sampling ${COUNT}/${total} files with seed ${SEED} ..."
  echo "[4/5] Converting to wav and writing manifest ..."

  local idx=0
  for src in "${sampled[@]}"; do
    idx=$((idx + 1))
    local utt_id
    utt_id=$(basename "${src}" .flac)
    local case_id
    case_id=$(printf 'dev-clean-2-%04d' "${idx}")
    local wav_name="${case_id}.wav"
    local wav_path="${OUTPUT_DIR}/audio/${wav_name}"

    transcode_to_wav "${src}" "${wav_path}"

    local ref="${transcript_by_id[${utt_id}]:-}"
    # Keep tsv single-line fields.
    ref=$(printf '%s' "${ref}" | tr '\t\n\r' '   ')

    printf '%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s\n' \
      "${case_id}" \
      "quiet" \
      "audio/${wav_name}" \
      "${ref}" \
      "endpointing" \
      "false" \
      "false" \
      "mini-librispeech-dev-clean-2 sampled" \
      "${src}" >> "${manifest}"
  done

  echo "[5/5] Done."
  echo "Output dir : ${OUTPUT_DIR}"
  echo "Manifest   : ${manifest}"
  echo "Audio files: ${COUNT}"
}

main "$@"
