// Copyright (c) Microsoft. All rights reserved.

import { useMsal } from '@azure/msal-react';
import { Persona, Text, makeStyles, mergeClasses, shorthands, tokens } from '@fluentui/react-components';
import React from 'react';
import { AuthorRoles, ChatMessageState, IChatMessage } from '../../libs/models/ChatMessage';
import { useChat } from '../../libs/useChat';
import { parsePlan } from '../../libs/utils/PlanUtils';
import { useAppDispatch, useAppSelector } from '../../redux/app/hooks';
import { RootState } from '../../redux/app/store';
import { updateMessageState } from '../../redux/features/conversations/conversationsSlice';
import { Breakpoints } from '../../styles';
import { convertToAnchorTags } from '../utils/TextUtils';
import { PlanViewer } from './plan-viewer/PlanViewer';
import { PromptDetails } from './prompt-details/PromptDetails';

const useClasses = makeStyles({
    root: {
        display: 'flex',
        flexDirection: 'row',
        maxWidth: '75%',
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        ...Breakpoints.small({
            maxWidth: '100%',
        }),
    },
    debug: {
        position: 'absolute',
        top: '-4px',
        right: '-4px',
    },
    alignEnd: {
        alignSelf: 'flex-end',
    },
    persona: {
        paddingTop: tokens.spacingVerticalS,
    },
    item: {
        backgroundColor: tokens.colorNeutralBackground1,
        ...shorthands.borderRadius(tokens.borderRadiusMedium),
        ...shorthands.padding(tokens.spacingVerticalS, tokens.spacingHorizontalL),
    },
    me: {
        backgroundColor: tokens.colorBrandBackground2,
    },
    time: {
        color: tokens.colorNeutralForeground3,
        fontSize: '12px',
        fontWeight: 400,
    },
    header: {
        position: 'relative',
        display: 'flex',
        flexDirection: 'row',
        ...shorthands.gap(tokens.spacingHorizontalL),
    },
    content: {
        wordBreak: 'break-word',
    },
    canvas: {
        width: '100%',
        textAlign: 'center',
    },
});

interface ChatHistoryItemProps {
    message: IChatMessage;
    getResponse: (
        value: string,
        approvedPlanJson?: string,
        planUserIntent?: string,
        userCancelledPlan?: boolean,
    ) => Promise<void>;
    messageIndex: number;
}

const createCommandLink = (command: string) => {
    const escapedCommand = encodeURIComponent(command);
    return `<span style="text-decoration: underline; cursor: pointer" data-command="${escapedCommand}" onclick="(function(){ let chatInput = document.getElementById('chat-input'); chatInput.value = decodeURIComponent('${escapedCommand}'); chatInput.focus(); return false; })();return false;">${command}</span>`;
};

export const ChatHistoryItem: React.FC<ChatHistoryItemProps> = ({ message, getResponse, messageIndex }) => {
    const classes = useClasses();

    const { instance } = useMsal();
    const account = instance.getActiveAccount();

    const chat = useChat();
    const { conversations, selectedId } = useAppSelector((state: RootState) => state.conversations);
    const dispatch = useAppDispatch();

    const plan = parsePlan(message.content);
    const isPlan = plan !== null;

    // Initializing Plan action handlers here so we don't have to drill down data the components won't use otherwise
    const onPlanApproval = async () => {
        dispatch(
            updateMessageState({
                newMessageState: ChatMessageState.PlanApproved,
                messageIndex: messageIndex,
                chatId: selectedId,
            }),
        );

        // Extract plan from bot response
        const proposedPlan = JSON.parse(message.content).proposedPlan;

        // Invoke plan
        await getResponse('Yes, proceed', JSON.stringify(proposedPlan), plan?.userIntent);
    };

    const onPlanCancel = async () => {
        dispatch(
            updateMessageState({
                newMessageState: ChatMessageState.PlanRejected,
                messageIndex: messageIndex,
                chatId: selectedId,
            }),
        );

        // Bail out of plan
        await getResponse('No, cancel', undefined, undefined, true);
    };

    const content = !isPlan
        ? (message.content as string)
              .trim()
              .replace(/[\u00A0-\u9999<>&]/g, function (i: string) {
                  return `&#${i.charCodeAt(0)};`;
              })
              .replace(/^sk:\/\/.*$/gm, (match: string) => createCommandLink(match))
              .replace(/^!sk:.*$/gm, (match: string) => createCommandLink(match))
              .replace(/\n/g, '<br />')
              .replace(/ {2}/g, '&nbsp;&nbsp;')
        : '';

    const date = new Date(message.timestamp);
    let time = date.toLocaleTimeString([], {
        hour: '2-digit',
        minute: '2-digit',
    });

    // If not today, prepend date
    if (date.toDateString() !== new Date().toDateString()) {
        time =
            date.toLocaleDateString([], {
                month: 'short',
                day: 'numeric',
            }) +
            ' ' +
            time;
    }

    const isMe = message.authorRole === AuthorRoles.User || message.userId === account?.homeAccountId!;
    const isBot = message.authorRole !== AuthorRoles.User && message.userId === 'bot';
    const user = chat.getChatUserById(message.userName, selectedId, conversations[selectedId].users);
    const fullName = user?.fullName ?? message.userName;

    const avatar = isBot
        ? { image: { src: conversations[selectedId].botProfilePicture } }
        : { name: fullName, color: 'colorful' as 'colorful' };

    return (
        <>
            <div className={isMe ? mergeClasses(classes.root, classes.alignEnd) : classes.root}>
                {!isMe && <Persona className={classes.persona} avatar={avatar} presence={{ status: 'available' }} />}
                <div className={isMe ? mergeClasses(classes.item, classes.me) : classes.item}>
                    <div className={classes.header}>
                        {!isMe && <Text weight="semibold">{fullName}</Text>}
                        <Text className={classes.time}>{time}</Text>
                        {isBot && <PromptDetails message={message} />}
                    </div>
                    {!isPlan && (
                        <div
                            className={classes.content}
                            dangerouslySetInnerHTML={{ __html: convertToAnchorTags(content) }}
                        />
                    )}
                    {isPlan && (
                        <PlanViewer
                            plan={plan}
                            planState={message.state ?? ChatMessageState.NoOp}
                            onSubmit={onPlanApproval}
                            onCancel={onPlanCancel}
                        />
                    )}
                </div>
            </div>
        </>
    );
};
