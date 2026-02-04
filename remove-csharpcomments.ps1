# 定义要处理的目录
$targetDirectory = "C:\原D\Scripts\Avalonia\Translationtool\03\TranslationToolUI"

# 获取目录下所有的C#文件
$csFiles = Get-ChildItem -Path $targetDirectory -Filter "*.cs" -Recurse

# 统计信息
$totalFiles = $csFiles.Count
$processedFiles = 0
$commentsRemoved = 0
$totalLinesModified = 0

# 创建备份目录
$backupDir = Join-Path $targetDirectory "backup"
if (-not (Test-Path $backupDir)) {
    New-Item -ItemType Directory -Path $backupDir | Out-Null
    Write-Host "创建备份目录: $backupDir" -ForegroundColor Green
}

# 创建修改报告目录
$reportDir = Join-Path $targetDirectory "修改报告"
if (-not (Test-Path $reportDir)) {
    New-Item -ItemType Directory -Path $reportDir | Out-Null
    Write-Host "创建修改报告目录: $reportDir" -ForegroundColor Green
}

# 定义一个函数来处理注释
function Remove-CSharpComments {
    param (
        [string]$content,
        [string]$filePath
    )
    
    # 保存原始内容以计算删除的注释量
    $originalLength = $content.Length
    $linesModified = 0
    
    # 用于详细报告的数组
    $modificationReport = @()
    
    # 步骤1: 处理块注释 /* ... */
    $blockCommentPattern = "(?s)/\*.*?\*/"
    # 保存块注释的位置和内容
    $blockMatches = [regex]::Matches($content, $blockCommentPattern)
    foreach ($match in $blockMatches) {
        $modificationReport += "删除块注释: $($match.Value)"
    }
    $content = [regex]::Replace($content, $blockCommentPattern, "")
    
    # 步骤2: 处理单行注释，采用更智能的方式判断
    $lines = $content -split "`r`n|`r|`n"
    $newLines = @()
    $lineNumber = 0
    
    foreach ($line in $lines) {
        $lineNumber++
        $trimmedLine = $line.TrimStart()
        
        # 情况1: 行首就是注释 (// 或 ///)，直接删除整行
        if ($trimmedLine.StartsWith("//")) {
            $linesModified++
            $modificationReport += "第 $lineNumber 行: 删除整行注释 [$line]"
            # 不添加这一行到 newLines，相当于删除
            continue
        }
        
        # 情况2: 行内有注释，需要判断是否在字符串中
        $commentIndex = $line.IndexOf("//")
        if ($commentIndex -ge 0) {
            # 分析这一行，保护字符串内的内容
            $result = ""
            $inString = $false
            $escaped = $false
            
            for ($i = 0; $i -lt $commentIndex; $i++) {
                $char = $line[$i]
                
                # 处理转义字符
                if ($char -eq '\' -and $inString -and -not $escaped) {
                    $escaped = $true
                    $result += $char
                    continue
                }
                
                # 处理字符串边界
                if ($char -eq '"' -and -not $escaped) {
                    $inString = -not $inString
                }
                
                # 重置转义标志
                if ($escaped) {
                    $escaped = $false
                }
                
                $result += $char
            }
            
            # 检查注释位置是否在字符串内
            if ($inString) {
                # 注释符号在字符串内，保留整行
                $newLines += $line
            }
            else {
                # 注释符号不在字符串内，删除注释部分
                $trimmedResult = $result.TrimEnd()
                $newLines += $trimmedResult
                $commentContent = $line.Substring($commentIndex)
                $modificationReport += "第 $lineNumber 行: 删除行内注释 [$commentContent] 保留 [$trimmedResult]"
                $linesModified++
            }
        }
        else {
            # 没有注释符号，保留原行
            $newLines += $line
        }
    }
    
    $content = $newLines -join "`r`n"
    
    # 步骤3: 处理XML文档注释 ///（应该已经在步骤2中处理，这里作为额外保障）
    $pattern = "^\s*///.*$"
    $content = [regex]::Replace($content, $pattern, "", [System.Text.RegularExpressions.RegexOptions]::Multiline)
    
    # 步骤4: 删除多余的空行，将两个或更多空行替换为一个空行
    $content = [regex]::Replace($content, "(\r?\n){3,}", "`r`n`r`n")
    
    # 计算删除的字符数
    $commentsRemoved = $originalLength - $content.Length
    
    return @{
        Content           = $content
        CommentsRemoved   = $commentsRemoved
        LinesModified     = $linesModified
        ModificationReport = $modificationReport
    }
}

Write-Host "开始处理 $totalFiles 个C#文件..." -ForegroundColor Cyan

foreach ($file in $csFiles) {
    $processedFiles++
    Write-Host "处理文件 [$processedFiles/$totalFiles]: $($file.FullName)" -ForegroundColor Yellow
    
    # 创建备份（保持原文件名）
    $backupPath = Join-Path $backupDir $file.Name
    
    # 如果同名备份已存在，添加数字后缀
    $counter = 1
    $finalBackupPath = $backupPath
    while (Test-Path $finalBackupPath) {
        $finalBackupPath = Join-Path $backupDir "$($file.BaseName)_$counter$($file.Extension)"
        $counter++
    }
    
    Copy-Item -Path $file.FullName -Destination $finalBackupPath
    Write-Host "  - 已创建备份: $finalBackupPath" -ForegroundColor Gray
    
    # 读取文件内容
    $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8
    
    # 处理文件内容
    $result = Remove-CSharpComments -content $content -filePath $file.FullName
    $newContent = $result.Content
    $fileCommentsRemoved = $result.CommentsRemoved
    $fileLinesModified = $result.LinesModified
    $modificationReport = $result.ModificationReport
    
    $commentsRemoved += $fileCommentsRemoved
    $totalLinesModified += $fileLinesModified
    
    # 写入新内容
    Set-Content -Path $file.FullName -Value $newContent -NoNewline:$false -Encoding UTF8
    
    Write-Host "  - 已删除大约 $fileCommentsRemoved 个字符的注释" -ForegroundColor Gray
    Write-Host "  - 已修改 $fileLinesModified 行代码" -ForegroundColor Gray
    
    # 显示具体修改内容（最多显示5条，避免输出过多）
    if ($modificationReport.Count -gt 0) {
        Write-Host "  - 修改内容示例:" -ForegroundColor Gray
        $displayCount = [Math]::Min(5, $modificationReport.Count)
        for ($i = 0; $i -lt $displayCount; $i++) {
            Write-Host "    * $($modificationReport[$i])" -ForegroundColor DarkGray
        }
        
        if ($modificationReport.Count -gt 5) {
            Write-Host "    * ... 还有 $($modificationReport.Count - 5) 处修改 ..." -ForegroundColor DarkGray
        }
    }
    
    # 生成详细修改报告
    $reportPath = Join-Path $reportDir "$($file.BaseName)_修改报告.txt"
    Set-Content -Path $reportPath -Value "文件修改报告: $($file.FullName)`r`n生成时间: $(Get-Date)`r`n`r`n已删除字符数: $fileCommentsRemoved`r`n修改行数: $fileLinesModified`r`n`r`n详细修改内容:`r`n" -Encoding UTF8
    Add-Content -Path $reportPath -Value ($modificationReport -join "`r`n") -Encoding UTF8
    
    Write-Host "  - 已生成详细修改报告: $reportPath" -ForegroundColor Gray
}

Write-Host "`n处理完成!" -ForegroundColor Green
Write-Host "总共处理了 $processedFiles 个文件，删除了大约 $commentsRemoved 个字符的注释" -ForegroundColor Green
Write-Host "共修改了 $totalLinesModified 行代码" -ForegroundColor Green
Write-Host "所有原始文件已备份到: $backupDir" -ForegroundColor Green
Write-Host "所有修改报告已保存到: $reportDir" -ForegroundColor Green

# 如果需要详细了解每行的修改，可以使用以下命令运行脚本：
# PowerShell -Verbose -File remove-csharpcomments.ps1

