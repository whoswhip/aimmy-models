const API_ENDPOINTS = {
    models: {
        primary: [
            "https://api.github.com/repos/whoswhip/aimmy-models/contents/models",
            "https://api.github.com/repos/Babyhamsta/aimmy/contents/models"
        ],
        backup: [
            "https://api.github.com/repos/whoswhip/aimmy/contents/models",
            "https://git.whoswhip.top/api/v1/repos/whoswhip/aimmy-models/contents/models",
        ]
    },
    configs: {
        primary: [
            "https://api.github.com/repos/whoswhip/aimmy-models/contents/configs",
            "https://api.github.com/repos/Babyhamsta/aimmy/contents/configs"
        ],
        backup: [
            "https://api.github.com/repos/whoswhip/aimmy/contents/configs",
            "https://git.whoswhip.top/api/v1/repos/whoswhip/aimmy-models/contents/configs",
        ]
    }
};
const CONFIG = {
    models: {
        title: "Every Aimmy Model",
        documentTitle: "Aimmy Models",
        linkHref: "?type=configs",
        linkText: "Configs"
    },
    configs: {
        title: "Every Aimmy Config",
        documentTitle: "Aimmy Configs",
        linkHref: "?type=models",
        linkText: "Models"
    }
};

let files = [];
let metadata = null;
let currentType = "models";
let contextMenuTarget = null;

document.addEventListener("DOMContentLoaded", () => {
    initializePage();
});

function initializePage() {
    const urlParams = new URLSearchParams(window.location.search);
    currentType = urlParams.get("type") || "models";

    updatePageForType(currentType);

    const hash = window.location.hash.substring(1).toLowerCase();
    if (hash) {
        document.getElementById("search").value = decodeURIComponent(hash);
    }
    fetchMetadata();
    getFiles().then(() => {
        if (hash) {
            search();
        }
        if (urlParams.has("model")) {
            let model = urlParams.get("model");
            let modelName = decodeURIComponent(model.split('@')[0]); // model filename
            let modelHash = model.split('@')[1];                     // first 6 characters of the hash
            let file = files.find(f => f.name === modelName && f.sha.startsWith(modelHash));
            // download file
            if (file) {
                window.open(file.download_url);
                window.location.assign(file.download_url);
            }
        }
    });
}

function updatePageForType(type) {
    const elements = {
        header: document.querySelector("h1"),
        typeLink: document.getElementById("cfg-onnx")
    };

    const typeConfig = CONFIG[type] || CONFIG.models;

    elements.header.textContent = typeConfig.title;
    document.title = typeConfig.documentTitle;
    elements.typeLink.href = typeConfig.linkHref;
    elements.typeLink.textContent = typeConfig.linkText;
}

async function getFiles() {
    const endpoints = API_ENDPOINTS[currentType];
    if (!endpoints) {
        console.error(`No endpoints configured for type: ${currentType}`);
        return;
    }

    files = [];
    document.getElementById("files").innerHTML = "";
    updateFileCount();

    try {
        const success = await fetchFromEndpoints(endpoints.primary);
        if (!success && endpoints.backup) {
            console.log('All primary endpoints failed, trying backup endpoints...');
            await fetchFromEndpoints(endpoints.backup);
        }

        files.sort((a, b) => a.name.localeCompare(b.name));
        files.forEach(model => {
            addFileToDOM(model);
        });
    } catch (error) {
        console.error('All endpoints failed:', error);
    }
}

async function fetchMetadata() {
    const metadataUrl = `https://raw.githubusercontent.com/whoswhip/aimmy-models/main/models/metadata.json`;
    try {
        const response = await fetch(metadataUrl);
        if (!response.ok) {
            throw new Error(`HTTP error status: ${response.status}`);
        }
        metadata = await response.json();
    } catch (error) {
        console.error('Failed to fetch metadata:', error);
    }
}

async function fetchFromEndpoints(urls) {
    let hasSuccess = false;
    const fetchPromises = urls.map(url =>
        fetchSingleEndpoint(url)
            .then(() => {
                hasSuccess = true;
            })
            .catch(error => {
                console.log(`Failed to fetch from ${url}:`, error);
            })
    );

    await Promise.allSettled(fetchPromises);
    return hasSuccess;
}

async function fetchSingleEndpoint(url) {
    const response = await fetch(url);
    if (!response.ok) {
        throw new Error(`HTTP error status: ${response.status}`);
    }

    const data = await response.json();
    const existingHashes = new Set(files.map(f => f.sha));
    const allowedExtensions = ['.onnx', '.pt', '.cfg'];

    data.forEach(model => {
        const hasAllowedExtension = allowedExtensions.some(ext =>
            model.name.toLowerCase().endsWith(ext)
        );

        if (hasAllowedExtension) {
            files.push(model);
            existingHashes.add(model.sha);
        }
    });

    updateFileCount();
}

function addFileToDOM(model) {
    const fileUrl = model.download_url;
    const fileName = model.name.replace(/_/g, ' ');
    const fileSize = formatBytes(model.size);
    const fileHtml = `<a href="${fileUrl}" style="display:flex;"><span style="width: 80%; display: inline-block;">${fileName}</span>  <span style="width: 20%; text-align: right; margin-left: auto; display: inline-block;">${fileSize}</span></a>`;
    const fileElement = document.createElement("li");
    fileElement.className = "file-item";
    fileElement.innerHTML = fileHtml;
    fileElement.setAttribute('data-sha', model.sha);
    fileElement.setAttribute('data-url', fileUrl);
    fileElement.setAttribute('data-name', model.name);
    fileElement.addEventListener('contextmenu', showContextMenu);
    document.getElementById("files").appendChild(fileElement);
}

function updateFileCount() {
    const typeLabel = currentType.charAt(0).toUpperCase() + currentType.slice(1);
    document.getElementById("count").innerHTML = `${files.length} ${typeLabel}`;
}

function search() {
    let search = document.getElementById("search").value.trim().toLowerCase();
    let filteredFiles = files;

    const filters = [
        {
            test: /!unique/,
            apply: (files) => Array.from(new Map(files.map(file => [file.sha, file])).values()),
            clean: (s) => s.replace("!unique", "").trim()
        },
        {
            test: /sort:size/,
            apply: (files) => files.slice().sort((a, b) => a.size - b.size),
            clean: (s) => s.replace("sort:size", "").trim()
        },
        {
            test: /sort:-size/,
            apply: (files) => files.slice().sort((a, b) => b.size - a.size),
            clean: (s) => s.replace("sort:-size", "").trim()
        },
        {
            test: /imagesize:(>=|<=|>|<|)?(\d+)/,
            apply: (files, match) => {
                if (!metadata) return files;
                const operator = match[1] || '=';
                const targetSize = parseInt(match[2]);
                return files.filter(file => {
                    const metaEntry = metadata.find(meta => meta.Hash === file.sha);
                    if (metaEntry && metaEntry.ImageSize && Array.isArray(metaEntry.ImageSize)) {
                        const imageSize = metaEntry.ImageSize[0];
                        switch (operator) {
                            case '>': return imageSize > targetSize;
                            case '>=': return imageSize >= targetSize;
                            case '<': return imageSize < targetSize;
                            case '<=': return imageSize <= targetSize;
                            default: return metaEntry.ImageSize.includes(targetSize);
                        }
                    }
                    return false;
                });
            },
            clean: (s) => s.replace(/imagesize:(>=|<=|>|<|)?\d+/, "").trim()
        },
        {
            test: /labels:(>=|<=|>|<|)?(\d+)/,
            apply: (files, match) => {
                if (!metadata) return files;
                const operator = match[1] || '=';
                const targetCount = parseInt(match[2]);
                return files.filter(file => {
                    const metaEntry = metadata.find(meta => meta.Hash === file.sha);
                    if (metaEntry && metaEntry.Labels) {
                        let labelCount = 0;
                        try {
                            let fixedLabels = metaEntry.Labels
                                .replace(/'/g, '"')
                                .replace(/(\d+):/g, '"$1":');
                            const labelsObj = JSON.parse(fixedLabels);
                            labelCount = Object.keys(labelsObj).length;
                        } catch (e) {
                            labelCount = 0;
                        }
                        switch (operator) {
                            case '>': return labelCount > targetCount;
                            case '>=': return labelCount >= targetCount;
                            case '<': return labelCount < targetCount;
                            case '<=': return labelCount <= targetCount;
                            default: return labelCount === targetCount;
                        }
                    }
                    return false;
                });
            },
            clean: (s) => s.replace(/labels:(>=|<=|>|<|)?\d+/, "").trim()
        },
        {
            test: /repo:([a-zA-Z0-9._-]+(?:\/[a-zA-Z0-9._-]+)?)/,
            apply: (files, match) => {
                const repoIdentifier = match[1].toLowerCase();
                return files.filter(file => {
                    const url = file.download_url.toLowerCase();
                    if (repoIdentifier.includes('/')) {
                        return url.includes(`/${repoIdentifier}/`);
                    }
                    const urlRepoMatch = url.match(/\/repos\/[^\/]+\/([^\/]+)\//);
                    if (urlRepoMatch) {
                        const urlRepoName = urlRepoMatch[1];
                        return urlRepoName === repoIdentifier || urlRepoName.includes(repoIdentifier);
                    }
                    return url.includes(repoIdentifier);
                });
            },
            clean: (s) => s.replace(/repo:[a-zA-Z0-9._\/-]+/, "").trim()
        },
        {
            test: /dynamic:(true|false)/,
            apply: (files, match) => {
                if (!metadata) return files;
                const isDynamic = match[1] === 'true';
                return files.filter(file => {
                    const metaEntry = metadata.find(meta => meta.Hash === file.sha);
                    if (!metaEntry) return false;
                    let argsDynamic = null;
                    if (metaEntry.Args) {
                        try {
                            let fixedArgs = metaEntry.Args
                                .replace(/'/g, '"')
                                .replace(/\bnone\b/gi, 'null')
                                .toLowerCase();
                            const argsObj = JSON.parse(fixedArgs);
                            argsDynamic = argsObj.dynamic;
                        } catch (e) {
                            argsDynamic = null;
                        }
                    }
                    if (argsDynamic !== null) {
                        return argsDynamic === isDynamic;
                    }
                    return metaEntry.Dynamic === isDynamic;
                });
            },
            clean: (s) => s.replace(/dynamic:(true|false)/, "").trim()
        },
        {
            test: /simplified:(true|false)/,
            apply: (files, match) => {
                if (!metadata) return files;
                const isSimplified = match[1] === 'true';
                return files.filter(file => {
                    const metaEntry = metadata.find(meta => meta.Hash === file.sha);
                    if (!metaEntry) return false;
                    let argsSimplified = null;
                    if (metaEntry.Args) {
                        try {
                            let fixedArgs = metaEntry.Args
                                .replace(/'/g, '"')
                                .replace(/\bnone\b/gi, 'null')
                                .toLowerCase();
                            const argsObj = JSON.parse(fixedArgs);
                            argsSimplified = argsObj.simplify;
                        } catch (e) {
                            argsSimplified = null;
                        }
                    }
                    if (argsSimplified !== null) {
                        return argsSimplified === isSimplified;
                    }
                    return metaEntry.Simplified === isSimplified;
                });
            },
            clean: (s) => s.replace(/simplified:(true|false)/, "").trim()
        },
        {
            test: /(?:githash|h):([a-z0-9]+)/,
            apply: (files, match) => files.filter(file => file.sha.includes(match[1])),
            clean: (s) => s.replace(/(?:githash|h):[a-z0-9]+/, "").trim()
        }
    ];

    for (const filter of filters) {
        let match = search.match(filter.test);
        if (match) {
            filteredFiles = filter.apply(filteredFiles, match);
            search = filter.clean(search);
        }
    }

    if (search.trim()) {
        filteredFiles = filteredFiles.filter(file => file.name.toLowerCase().includes(search));
    }

    document.getElementById("files").innerHTML = "";
    filteredFiles.sort((a, b) => a.name.localeCompare(b.name));
    filteredFiles.forEach(file => {
        addFileToDOM(file);
    });
    updateFileCount();
}

function showContextMenu(e) {
    e.preventDefault();
    contextMenuTarget = e.currentTarget;
    const contextMenu = document.getElementById('contextMenu');
    contextMenu.style.display = 'block';
    contextMenu.style.left = e.pageX + 'px';
    contextMenu.style.top = e.pageY + 'px';
}

function hideContextMenu() {
    document.getElementById('contextMenu').style.display = 'none';
    contextMenuTarget = null;
}

function downloadFile() {
    if (contextMenuTarget) {
        const url = contextMenuTarget.getAttribute('data-url');
        window.open(url, '_blank');
    }
    hideContextMenu();
}

function openInNetron() {
    if (contextMenuTarget) {
        const url = contextMenuTarget.getAttribute('data-url');
        const netronUrl = `https://netron.app/?url=${encodeURIComponent(url)}`;
        window.open(netronUrl, '_blank');
    }
    hideContextMenu();
}

function copyGitHash() {
    if (contextMenuTarget) {
        const sha = contextMenuTarget.getAttribute('data-sha');
        navigator.clipboard.writeText(sha)
            .catch(err => {
                console.error('Failed to copy GitHash:', err);
            });
    }
    hideContextMenu();
}

function copyFileUrl() {
    if (contextMenuTarget) {
        const url = contextMenuTarget.getAttribute('data-url');
        navigator.clipboard.writeText(url)
            .catch(err => {
                console.error('Failed to copy File URL:', err);
            });
    }
    hideContextMenu();
}

function copyShortUrl() {
    if (contextMenuTarget) {
        const url = contextMenuTarget.getAttribute('data-url').split('/').pop();
        const sha = contextMenuTarget.getAttribute('data-sha');
        const shortName = `${url}@${sha.substring(0, 6)}`;
        let shortUrl = `https://models.whoswhip.dev/?model=${shortName}#h:${sha}`;

        if (currentType == "configs") {
            shortUrl = `https://models.whoswhip.dev/?model=${shortName}&type=configs#h:${sha}`;
        }

        navigator.clipboard.writeText(shortUrl)
            .catch(err => {
                console.error('Failed to copy Short URL:', err);
            });
    }
    hideContextMenu();
}

document.addEventListener('click', hideContextMenu);
document.addEventListener('scroll', hideContextMenu);

function uniqueModal() {
    document.getElementById('uniqueModal').classList.add('active');
}

function closeUniqueModal() {
    document.getElementById('uniqueModal').classList.remove('active');
    document.getElementById('resultArea').classList.remove('show');
    document.getElementById('fileInput').value = '';
}

function handleFileUpload(event) {
    const file = event.target.files[0];
    if (!file) return;

    document.getElementById('resultText').textContent = 'Calculating hash...';
    document.getElementById('resultArea').classList.add('show');

    calculateSHA1(file).then(hash => {
        document.getElementById('hashText').textContent = `SHA-1: ${hash}`;

        const matchingFile = files.find(f => f.sha === hash);

        if (matchingFile) {
            document.getElementById('resultText').textContent = '❌ File is NOT unique';
            document.getElementById('resultText').style.color = '#ff6b6b';
            document.getElementById('matchInfo').innerHTML = `
                    <p style="color: #ccc; margin-top: 10px;">
                        <strong>Match found:</strong><br>
                        Name: ${matchingFile.name}<br>
                        Size: ${formatBytes(matchingFile.size)}<br>
                        Path: <a href="${matchingFile.download_url}" target="_blank">${matchingFile.path}</a>
                    </p>
                `;
        } else {
            document.getElementById('resultText').textContent = '✅ File appears to be unique';
            document.getElementById('resultText').style.color = '#51cf66';
            document.getElementById('matchInfo').innerHTML = `
                    <p style="color: #ccc; margin-top: 10px;">
                        No matching SHA-1 hash found in our database.
                    </p>
                `;
        }
    }).catch(error => {
        document.getElementById('resultText').textContent = '❌ Error calculating hash';
        document.getElementById('resultText').style.color = '#ff6b6b';
        document.getElementById('hashText').textContent = error.message;
    });
}

document.addEventListener('DOMContentLoaded', () => {

    const dropArea = document.querySelector('.file-drop-area');

    ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
        dropArea.addEventListener(eventName, preventDefaults, false);
    });

    function preventDefaults(e) {
        e.preventDefault();
        e.stopPropagation();
    }

    ['dragenter', 'dragover'].forEach(eventName => {
        dropArea.addEventListener(eventName, () => dropArea.classList.add('dragover'), false);
    });

    ['dragleave', 'drop'].forEach(eventName => {
        dropArea.addEventListener(eventName, () => dropArea.classList.remove('dragover'), false);
    });

    dropArea.addEventListener('drop', handleDrop, false);

    function handleDrop(e) {
        const dt = e.dataTransfer;
        const files = dt.files;

        if (files.length > 0) {
            document.getElementById('fileInput').files = files;
            handleFileUpload({ target: { files: files } });
        }
    }

    document.getElementById('uniqueModal').addEventListener('click', (e) => {
        if (e.target.id === 'uniqueModal') {
            closeUniqueModal();
        }
    });
});

async function calculateSHA1(file) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = async function (e) {
            try {
                const arrayBuffer = e.target.result;
                const fileContent = new Uint8Array(arrayBuffer);

                // git object format "blob <size>\0<content>"
                const header = `blob ${fileContent.length}\0`;
                const headerBytes = new TextEncoder().encode(header);

                const gitObject = new Uint8Array(headerBytes.length + fileContent.length);
                gitObject.set(headerBytes, 0);
                gitObject.set(fileContent, headerBytes.length);

                const hashBuffer = await crypto.subtle.digest('SHA-1', gitObject);
                const hashArray = Array.from(new Uint8Array(hashBuffer));
                const hashHex = hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
                resolve(hashHex);
            } catch (error) {
                reject(error);
            }
        };
        reader.onerror = function () {
            reject(new Error('Failed to read file'));
        };
        reader.readAsArrayBuffer(file);
    });
}

function formatBytes(bytes) {
    const units = ['bytes', 'KB', 'MB', 'GB', 'TB'];
    let unitIndex = 0;
    while (bytes >= 1024 && unitIndex < units.length - 1) {
        bytes /= 1024;
        unitIndex++;
    }
    return `${bytes.toFixed(2)} ${units[unitIndex]}`;
}
