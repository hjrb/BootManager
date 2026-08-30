<#
.SYNOPSIS
	Converts a Markdown document into a single self-contained HTML file.

.DESCRIPTION
	Written for the documents this repository ships to end users, who cannot be expected to have a
	Markdown viewer at hand. The output is one file with the stylesheet embedded, so it opens in any
	browser without an internet connection and without any accompanying assets.

	This is deliberately not a complete Markdown implementation. It understands exactly the constructs
	the repository's documents use - headings, paragraphs, tables, lists, block quotes, fenced code
	blocks, horizontal rules, and inline code, emphasis, links and images - and nothing else. Adding a
	Markdown library only to produce a README would mean shipping that library with the application.

	Relative links are rewritten to point at the repository on GitHub. In a file that travels on its own
	next to the executable, a link to 'SECURITY.md' would otherwise lead nowhere.

	Heading anchors follow GitHub's rules (lower case, punctuation dropped, spaces turned into hyphens),
	so a link such as '#why-dont-i-see-my-usb-stick-or-dvd' resolves in the generated file exactly as it
	does on GitHub.

	The script only reads its input and writes its output; it runs on Windows PowerShell 5.1 and on
	PowerShell 7, because the release workflow uses the latter and developers usually the former.

.PARAMETER Path
	The Markdown file to convert.

.PARAMETER Destination
	The HTML file to write. An existing file is overwritten.

.PARAMETER Title
	Text for the browser's title bar. Defaults to the file name without its extension.

.PARAMETER BaseUrl
	Prefix put in front of relative links. Must end with a slash.

.EXAMPLE
	.\Convert-MarkdownToHtml.ps1 -Path README.md -Destination README.html
#>
[CmdletBinding()]
param(
	[Parameter(Mandatory)]
	[string] $Path,

	[Parameter(Mandatory)]
	[string] $Destination,

	[string] $Title,

	[ValidatePattern('/$')]
	[string] $BaseUrl = 'https://github.com/hjrb/BootManager/blob/main/'
)

$ErrorActionPreference = 'Stop'

# Kept deliberately plain: a readable measure, a legible font, and enough contrast on tables and code
# to tell them apart from the running text. No colours that would look wrong in a dark browser theme.
$styleSheet = @'
	:root { color-scheme: light dark; }
	body {
		font-family: -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
		line-height: 1.6;
		max-width: 46rem;
		margin: 0 auto;
		padding: 2rem 1.25rem 4rem;
	}
	h1, h2, h3, h4 { line-height: 1.25; margin-top: 2rem; }
	h1, h2 { border-bottom: 1px solid rgba(128, 128, 128, 0.35); padding-bottom: 0.3rem; }
	code, pre { font-family: Consolas, "SF Mono", "DejaVu Sans Mono", monospace; font-size: 0.9em; }
	code { background: rgba(128, 128, 128, 0.18); padding: 0.1em 0.35em; border-radius: 4px; }
	pre { background: rgba(128, 128, 128, 0.14); padding: 0.9rem 1rem; border-radius: 6px; overflow-x: auto; }
	pre code { background: none; padding: 0; }
	table { border-collapse: collapse; width: 100%; margin: 1rem 0; }
	th, td { border: 1px solid rgba(128, 128, 128, 0.45); padding: 0.4rem 0.6rem; text-align: left; vertical-align: top; }
	th { background: rgba(128, 128, 128, 0.18); }
	blockquote {
		margin: 1rem 0;
		padding: 0.2rem 1rem;
		border-left: 4px solid rgba(128, 128, 128, 0.5);
		background: rgba(128, 128, 128, 0.08);
	}
	hr { border: none; border-top: 1px solid rgba(128, 128, 128, 0.35); margin: 2rem 0; }
	img { max-width: 100%; vertical-align: middle; }
'@

function ConvertTo-EscapedHtml {
	param([string] $Text)

	# Ampersand first: doing it later would escape the ampersands of the entities written before it.
	return $Text.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
}

function ConvertTo-InlineHtml {
	<#
		.SYNOPSIS
			Renders the inline markup of a single run of text.
		.DESCRIPTION
			Code spans are pulled out first and put back last. Their content must survive verbatim -
			'**' inside backticks is two asterisks, not the start of bold text - and the placeholder
			that stands in for them contains no character any later step reacts to.
	#>
	param([string] $Text)

	$codeSpans = New-Object System.Collections.Generic.List[string]
	$builder = New-Object System.Text.StringBuilder
	$position = 0

	foreach ($match in [regex]::Matches($Text, '`([^`]+)`')) {
		[void] $builder.Append($Text.Substring($position, $match.Index - $position))
		[void] $builder.Append("%%CODE$($codeSpans.Count)%%")
		$codeSpans.Add($match.Groups[1].Value)
		$position = $match.Index + $match.Length
	}

	[void] $builder.Append($Text.Substring($position))
	$result = ConvertTo-EscapedHtml -Text $builder.ToString()

	# Anything that is not an absolute URL or an anchor is a file in the repository.
	$result = [regex]::Replace($result, '\]\((?!https?://|#|mailto:)([^)]+)\)', "](${BaseUrl}`$1)")

	# Images before links: a badge is a link whose text is an image, and the link pattern would
	# otherwise swallow the leading exclamation mark's image.
	$result = $result -replace '!\[([^\]]*)\]\(([^)]+)\)', '<img src="$2" alt="$1" />'
	$result = $result -replace '\[([^\]]+)\]\(([^)]+)\)', '<a href="$2">$1</a>'
	$result = $result -replace '\*\*([^*]+)\*\*', '<strong>$1</strong>'
	$result = $result -replace '\*([^*]+)\*', '<em>$1</em>'

	for ($index = $codeSpans.Count - 1; $index -ge 0; $index--) {
		$code = ConvertTo-EscapedHtml -Text $codeSpans[$index]
		$result = $result.Replace("%%CODE$index%%", "<code>$code</code>")
	}

	return $result
}

function Get-HeadingId {
	<#
		.SYNOPSIS
			Builds the anchor of a heading the way GitHub does.
		.DESCRIPTION
			Markup is stripped, everything is lower-cased, every character that is not a letter, digit,
			space or hyphen is dropped, and the remaining spaces become hyphens. Matching GitHub matters
			because the application links into the README by anchor.
	#>
	param([string] $Text)

	$plain = $Text -replace '`', '' -replace '\[([^\]]+)\]\([^)]+\)', '$1' -replace '\*', ''
	$slug = $plain.ToLowerInvariant() -replace '[^a-z0-9 \-]', '' -replace '\s+', '-'
	return $slug.Trim('-')
}

function Test-BlockStart {
	<#
		.SYNOPSIS
			Whether a line begins a new block, and therefore ends the paragraph or list item before it.
	#>
	param([string] $Line)

	return $Line -match '^\s*$' `
		-or $Line -match '^#{1,6}\s' `
		-or $Line -match '^\s*```' `
		-or $Line -match '^\s*>' `
		-or $Line -match '^\s*\|' `
		-or $Line -match '^(-{3,}|\*{3,}|_{3,})\s*$' `
		-or $Line -match '^\s*[-*+]\s' `
		-or $Line -match '^\s*\d+\.\s'
}

if (-not (Test-Path -Path $Path)) {
	throw "Markdown file not found at '$Path'."
}

if (-not $Title) {
	$Title = [System.IO.Path]::GetFileNameWithoutExtension($Path)
}

$lines = (Get-Content -Path $Path -Raw) -split '\r?\n'
$body = New-Object System.Text.StringBuilder
$lineNumber = 0

while ($lineNumber -lt $lines.Count) {
	$line = $lines[$lineNumber]

	# Fenced code block. Its content is taken verbatim, which is why it is tested before everything
	# else: inside it, a '#' is a comment and a '|' is a pipe, not a heading and not a table.
	if ($line -match '^\s*```') {
		$lineNumber++
		$code = New-Object System.Collections.Generic.List[string]
		while ($lineNumber -lt $lines.Count -and $lines[$lineNumber] -notmatch '^\s*```') {
			$code.Add($lines[$lineNumber])
			$lineNumber++
		}

		# Steps past the closing fence, or past the end of the file if the fence was never closed.
		$lineNumber++
		$escaped = ConvertTo-EscapedHtml -Text ($code -join "`n")
		[void] $body.AppendLine("<pre><code>$escaped</code></pre>")
		continue
	}

	if ($line -match '^\s*$') {
		$lineNumber++
		continue
	}

	if ($line -match '^(#{1,6})\s+(.+?)\s*#*\s*$') {
		$level = $Matches[1].Length
		$text = $Matches[2]
		$id = Get-HeadingId -Text $text
		[void] $body.AppendLine("<h$level id=""$id"">$(ConvertTo-InlineHtml -Text $text)</h$level>")
		$lineNumber++
		continue
	}

	if ($line -match '^(-{3,}|\*{3,}|_{3,})\s*$') {
		[void] $body.AppendLine('<hr />')
		$lineNumber++
		continue
	}

	# A table is only a table when the second line is the dashed separator; a single line starting
	# with a pipe is just a paragraph.
	if ($line -match '^\s*\|' -and
		$lineNumber + 1 -lt $lines.Count -and
		$lines[$lineNumber + 1] -match '^\s*\|[\s:|-]+\|\s*$') {

		$headerCells = ($line.Trim().Trim('|') -split '\|') | ForEach-Object { ConvertTo-InlineHtml -Text $_.Trim() }
		[void] $body.AppendLine('<table>')
		[void] $body.AppendLine("<thead><tr>$(($headerCells | ForEach-Object { "<th>$_</th>" }) -join '')</tr></thead>")
		[void] $body.AppendLine('<tbody>')

		$lineNumber += 2
		while ($lineNumber -lt $lines.Count -and $lines[$lineNumber] -match '^\s*\|') {
			$cells = ($lines[$lineNumber].Trim().Trim('|') -split '\|') | ForEach-Object { ConvertTo-InlineHtml -Text $_.Trim() }
			[void] $body.AppendLine("<tr>$(($cells | ForEach-Object { "<td>$_</td>" }) -join '')</tr>")
			$lineNumber++
		}

		[void] $body.AppendLine('</tbody></table>')
		continue
	}

	if ($line -match '^\s*>') {
		$quoted = New-Object System.Collections.Generic.List[string]
		while ($lineNumber -lt $lines.Count -and $lines[$lineNumber] -match '^\s*>') {
			$quoted.Add(($lines[$lineNumber] -replace '^\s*>\s?', ''))
			$lineNumber++
		}

		$text = ConvertTo-InlineHtml -Text (($quoted -join ' ').Trim())
		[void] $body.AppendLine("<blockquote><p>$text</p></blockquote>")
		continue
	}

	if ($line -match '^\s*([-*+]|\d+\.)\s') {
		$ordered = $line -match '^\s*\d+\.\s'
		$tag = if ($ordered) { 'ol' } else { 'ul' }
		$itemPattern = if ($ordered) { '^\s*\d+\.\s+(.*)$' } else { '^\s*[-*+]\s+(.*)$' }

		[void] $body.AppendLine("<$tag>")
		while ($lineNumber -lt $lines.Count -and $lines[$lineNumber] -match $itemPattern) {
			$item = $Matches[1]
			$lineNumber++

			# An indented follow-up line belongs to the item above it; the author wrapped the text.
			while ($lineNumber -lt $lines.Count -and -not (Test-BlockStart -Line $lines[$lineNumber])) {
				$item += ' ' + $lines[$lineNumber].Trim()
				$lineNumber++
			}

			[void] $body.AppendLine("<li>$(ConvertTo-InlineHtml -Text $item)</li>")
		}

		[void] $body.AppendLine("</$tag>")
		continue
	}

	$paragraph = New-Object System.Collections.Generic.List[string]
	$paragraph.Add($line.Trim())
	$lineNumber++
	while ($lineNumber -lt $lines.Count -and -not (Test-BlockStart -Line $lines[$lineNumber])) {
		$paragraph.Add($lines[$lineNumber].Trim())
		$lineNumber++
	}

	[void] $body.AppendLine("<p>$(ConvertTo-InlineHtml -Text ($paragraph -join ' '))</p>")
}

$document = @"
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>$(ConvertTo-EscapedHtml -Text $Title)</title>
<style>
$styleSheet
</style>
</head>
<body>
$($body.ToString().TrimEnd())
</body>
</html>
"@

if (-not [System.IO.Path]::IsPathRooted($Destination)) {
	# WriteAllText resolves relative paths against the process directory, which is not necessarily the
	# location the caller is working in.
	$Destination = Join-Path (Get-Location).ProviderPath $Destination
}

$destinationDirectory = Split-Path -Path $Destination -Parent
if ($destinationDirectory -and -not (Test-Path -Path $destinationDirectory)) {
	New-Item -Path $destinationDirectory -ItemType Directory -Force | Out-Null
}

# No byte order mark: some browsers and editors show it as stray characters at the top of the file.
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($Destination, $document, $utf8WithoutBom)

Write-Verbose "Wrote $Destination"
