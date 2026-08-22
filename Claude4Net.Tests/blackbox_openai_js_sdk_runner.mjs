import fs from 'node:fs';

const gate = Buffer.alloc(1);
const gateBytesRead = fs.readSync(0, gate, 0, 1, null);
if (gateBytesRead !== 1 || gate[0] !== 1) {
    process.exit(1);
}
const { default: OpenAI } = await import('openai');

async function runJsBlackBoxTests() {
    const args = process.argv.slice(2);
    let port = 7836;
    const environmentApiKey = process.env.CLAUDE4NET_TEST_API_KEY;
    let apiKey = environmentApiKey || 'c4n-sk-test';

    for (let i = 0; i < args.length; i++) {
        if (args[i] === '--port' && i + 1 < args.length) {
            port = parseInt(args[i + 1], 10);
            i++;
        } else if (args[i] === '--api-key' && i + 1 < args.length) {
            if (!environmentApiKey) {
                apiKey = args[i + 1];
            }
            i++;
        }
    }

    const baseURL = `http://127.0.0.1:${port}/v1`;
    console.log(`=== Starting Official OpenAI JavaScript SDK (v${OpenAI.VERSION || '7.4.0'}) Black-Box Suite ===`);
    console.log(`Target Base URL: ${baseURL}`);

    const client = new OpenAI({
        baseURL: baseURL,
        apiKey: apiKey
    });

    let passedTests = 0;
    let totalTests = 0;

    // 1. Models List
    totalTests++;
    console.log('\n[Test 1] GET /v1/models (List Models)...');
    const modelsList = await client.models.list();
    if (!modelsList.data || modelsList.data.length === 0) {
        throw new Error('Models list returned empty');
    }
    const modelIds = modelsList.data.map(m => m.id);
    console.log(`  Found ${modelIds.length} models. Sample: ${modelIds.slice(0, 3).join(', ')}`);
    if (!modelIds.includes('claude-3-5-sonnet-20241022') && !modelIds.includes('gpt-4o')) {
        throw new Error('Expected model cards not found');
    }
    passedTests++;
    console.log('  [PASS] Models list succeeded.');

    // 2. Model Retrieve (Valid)
    totalTests++;
    console.log('\n[Test 2] GET /v1/models/{model} (Retrieve Valid Model)...');
    const modelCard = await client.models.retrieve('claude-3-5-sonnet-20241022');
    if (modelCard.id !== 'claude-3-5-sonnet-20241022' || modelCard.owned_by !== 'anthropic') {
        throw new Error(`Invalid model card: ${JSON.stringify(modelCard)}`);
    }
    passedTests++;
    console.log(`  [PASS] Retrieved model card: id=${modelCard.id}, owned_by=${modelCard.owned_by}`);

    // 3. Model Retrieve (404 NotFound)
    totalTests++;
    console.log('\n[Test 3] GET /v1/models/{model} (404 NotFound)...');
    try {
        await client.models.retrieve('non-existent-model-xyz-999');
        throw new Error('Expected 404 NotFound error was not thrown');
    } catch (err) {
        if (err.status !== 404 && err.name !== 'NotFoundError') {
            throw new Error(`Expected NotFoundError (404) but got ${err}`);
        }
        passedTests++;
        console.log(`  [PASS] Correctly received 404 NotFoundError: ${err.message}`);
    }

    // 4. Chat Completion Non-Streaming
    totalTests++;
    console.log('\n[Test 4] POST /v1/chat/completions (Non-Streaming)...');
    const chatResp = await client.chat.completions.create({
        model: 'claude-3-5-sonnet',
        messages: [{ role: 'user', content: 'Hello official OpenAI JS SDK' }],
        max_completion_tokens: 50
    });
    if (!chatResp.id.startsWith('chatcmpl-') || !chatResp.choices || chatResp.choices.length === 0) {
        throw new Error(`Invalid chat response structure: ${JSON.stringify(chatResp)}`);
    }
    if (chatResp.choices[0].message.role !== 'assistant') {
        throw new Error(`Expected assistant role but got: ${chatResp.choices[0].message.role}`);
    }
    if (!chatResp.usage || chatResp.usage.total_tokens <= 0) {
        throw new Error('Usage was not returned');
    }
    passedTests++;
    console.log(`  [PASS] Non-streaming response content: "${chatResp.choices[0].message.content.slice(0, 35)}..."`);

    // 5. Chat Completion Streaming & stream_options.include_usage
    totalTests++;
    console.log('\n[Test 5] POST /v1/chat/completions (Streaming with Usage Chunk)...');
    const stream = await client.chat.completions.create({
        model: 'claude-3-5-sonnet',
        messages: [{ role: 'user', content: 'Stream message via JS SDK' }],
        stream: true,
        stream_options: { include_usage: true }
    });

    let chunksCount = 0;
    let hasUsageChunk = false;
    for await (const chunk of stream) {
        chunksCount++;
        if (chunk.usage && chunk.usage.total_tokens > 0) {
            hasUsageChunk = true;
            console.log(`  Received usage chunk: prompt=${chunk.usage.prompt_tokens}, completion=${chunk.usage.completion_tokens}`);
        }
    }
    if (chunksCount === 0 || !hasUsageChunk) {
        throw new Error(`Expected streaming chunks with usage chunk but got chunks=${chunksCount}, hasUsage=${hasUsageChunk}`);
    }
    passedTests++;
    console.log('  [PASS] Streaming with usage chunk succeeded.');

    // 6. Tool Calls Streaming & Accumulation
    totalTests++;
    console.log('\n[Test 6] POST /v1/chat/completions (Streaming Tool Calls)...');
    const toolStream = await client.chat.completions.create({
        model: 'claude-3-5-sonnet',
        messages: [{ role: 'user', content: 'invoke tool calculator with number 42' }],
        tools: [
            {
                type: 'function',
                function: {
                    name: 'calculator',
                    description: 'Calculates math expressions',
                    parameters: {
                        type: 'object',
                        properties: {
                            number: { type: 'string' }
                        },
                        required: ['number']
                    }
                }
            }
        ],
        stream: true
    });

    const accumulatedTools = {};
    let sawToolCallsFinish = false;
    for await (const chunk of toolStream) {
        const choice = chunk.choices && chunk.choices[0];
        if (choice && choice.delta && choice.delta.tool_calls) {
            for (const tc of choice.delta.tool_calls) {
                const idx = tc.index || 0;
                if (!accumulatedTools[idx]) {
                    accumulatedTools[idx] = { id: tc.id || '', name: '', arguments: '' };
                }
                if (tc.id) accumulatedTools[idx].id = tc.id;
                if (tc.function && tc.function.name) accumulatedTools[idx].name += tc.function.name;
                if (tc.function && tc.function.arguments) accumulatedTools[idx].arguments += tc.function.arguments;
            }
        }
        if (choice && choice.finish_reason === 'tool_calls') {
            sawToolCallsFinish = true;
        }
    }

    if (!sawToolCallsFinish || !accumulatedTools[0]) {
        throw new Error('Tool calls stream failed to accumulate or complete with finish_reason: tool_calls');
    }
    const parsedArgs = JSON.parse(accumulatedTools[0].arguments);
    if (parsedArgs.number !== '42' || accumulatedTools[0].name !== 'calculator') {
        throw new Error(`Tool call reassembly mismatch: ${JSON.stringify(accumulatedTools[0])}`);
    }
    passedTests++;
    console.log(`  [PASS] Reassembled tool call: name=${accumulatedTools[0].name}, args=${accumulatedTools[0].arguments}`);

    // 7. Reasoning Content Extraction
    totalTests++;
    console.log('\n[Test 7] POST /v1/chat/completions (Reasoning Content)...');
    const rStream = await client.chat.completions.create({
        model: 'gemini-2.5-flash',
        messages: [{ role: 'user', content: 'reasoning test please' }],
        stream: true
    });

    let contentStr = '';
    let sawReasoning = false;
    for await (const chunk of rStream) {
        const choice = chunk.choices && chunk.choices[0];
        if (choice && choice.delta) {
            if (choice.delta.reasoning_content) {
                sawReasoning = true;
            }
            if (choice.delta.content) {
                contentStr += choice.delta.content;
            }
        }
    }
    if (!sawReasoning) {
        throw new Error('Expected reasoning_content field in stream deltas');
    }
    if (contentStr.includes('<think>') || contentStr.includes('</think>')) {
        throw new Error(`Tag leakage in content: "${contentStr}"`);
    }
    passedTests++;
    console.log(`  [PASS] Reasoning content isolated without leaking tags. Content: "${contentStr}"`);

    // 8. Embeddings
    totalTests++;
    console.log('\n[Test 8] POST /v1/embeddings (Float Vector)...');
    const embResp = await client.embeddings.create({
        model: 'text-embedding-004',
        input: 'Hello JS SDK embedding',
        dimensions: 768
    });
    if (!embResp.data || embResp.data.length === 0 || embResp.data[0].embedding.length !== 768) {
        throw new Error(`Invalid embedding response: ${JSON.stringify(embResp)}`);
    }
    passedTests++;
    console.log(`  [PASS] Embeddings returned valid 768-dim float array.`);

    // 9. Embeddings Unsupported Dimension Rejection (400)
    totalTests++;
    console.log('\n[Test 9] POST /v1/embeddings (Unsupported Dimension Rejection 400)...');
    try {
        await client.embeddings.create({
            model: 'text-embedding-004',
            input: 'Test dim reject',
            dimensions: 128
        });
        throw new Error('Expected 400 BadRequestError for unsupported dimensions');
    } catch (err) {
        if (err.status !== 400 && err.name !== 'BadRequestError') {
            throw new Error(`Expected BadRequestError (400) but got ${err}`);
        }
        passedTests++;
        console.log(`  [PASS] Correctly rejected with 400 BadRequestError: ${err.message}`);
    }

    // 10. Authentication Error (401)
    totalTests++;
    console.log('\n[Test 10] Authentication Error (401 on Invalid API Key)...');
    const badClient = new OpenAI({
        baseURL: baseURL,
        apiKey: 'wrong-key-xyz'
    });
    try {
        await badClient.models.list();
        throw new Error('Expected 401 AuthenticationError');
    } catch (err) {
        if (err.status !== 401 && err.name !== 'AuthenticationError') {
            throw new Error(`Expected AuthenticationError (401) but got ${err}`);
        }
        passedTests++;
        console.log(`  [PASS] Correctly received 401 AuthenticationError: ${err.message}`);
    }

    // 11. Concurrency (10 requests)
    totalTests++;
    console.log('\n[Test 11] Concurrency Test (10 Concurrent Requests)...');
    const promises = Array.from({ length: 10 }, (_, i) =>
        client.chat.completions.create({
            model: 'claude-3-5-sonnet',
            messages: [{ role: 'user', content: `JS Concurrency probe ${i}` }],
            max_completion_tokens: 20
        })
    );
    const results = await Promise.all(promises);
    if (results.length !== 10 || results.some(r => !r.choices || r.choices.length === 0)) {
        throw new Error('Concurrency test failed');
    }
    passedTests++;
    console.log('  [PASS] 10 concurrent requests completed successfully.');

    totalTests++;
    console.log('\n[Test 12] POST /v1/completions (Legacy Completion)...');
    const completionModel = 'claude-3-5-sonnet';
    const completionResp = await client.completions.create({
        model: completionModel,
        prompt: 'Complete this official JavaScript SDK compatibility probe',
        max_tokens: 50
    });
    if (!completionResp.id.startsWith('cmpl-')) {
        throw new Error(`Invalid legacy completion id: ${completionResp.id}`);
    }
    if (completionResp.model !== completionModel) {
        throw new Error(`Legacy completion model mismatch: ${completionResp.model}`);
    }
    const completionChoice = completionResp.choices && completionResp.choices[0];
    if (!completionChoice || !completionChoice.text || !completionChoice.finish_reason) {
        throw new Error(`Invalid legacy completion choice: ${JSON.stringify(completionResp)}`);
    }
    if (!completionResp.usage || completionResp.usage.total_tokens <= 0) {
        throw new Error('Legacy completion usage was not returned');
    }
    passedTests++;
    console.log('  [PASS] Legacy completion returned SDK-compatible text and usage.');

    console.log('\n=======================================================');
    console.log(`ALL ${passedTests}/${totalTests} OFFICIAL OPENAI JS SDK BLACK-BOX TESTS PASSED!`);
    console.log('=======================================================');
}

runJsBlackBoxTests().catch(err => {
    console.error('JS Black-Box Test Failed:', err);
    process.exit(1);
});
